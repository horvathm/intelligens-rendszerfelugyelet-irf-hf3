﻿//2016 IRF HF3 3H kódú házi feladat
//===============================================================================
//Name: Grapevine.Example.Server.Program
//Author: Mate Horvath, CEKV8V
//Date: 2016.05.08.
//===============================================================================
//Desc:
//===============================================================================

using Grapevine.Client;
using Grapevine.Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Grapevine.Example
{
    class Program
    {
        static void Main(string[] args)
        {
            if (!PerformanceCounterCategory.Exists(Counters.CategoryName))
            {
                Console.WriteLine(Resources.PerformanceCountersNotExistUIMessage);
                Console.ReadKey();
                return;
            }

            //Testing of 2 NumberOfItems64 counters (NOI64NSTran, NOI64STran)
            Test1();
            //Testing of the AverageCounter64 counters (AC64PostReqBase,AC64PostReq)
            Test2();
            //Testing of the RateOfCountsPerSecond32 counter (ROCPS64Flush)
            Test3();
            //Testing of the Get and Post request counters (NOI64PostReqCount,NOI64GetReqCount)
            Test4();
            Console.ReadKey();
        }

        /// <summary>
        /// Beerkezo Get es Post keresek szamlaloinak tesztelese.
        /// Elkuldunk 20 kerest, veletlenszeruen legyen POST vagy GET. Lepesenkent kiirjuk a szamlalo erteket
        /// es varakozunk valameddig, hogy Performance Monitoron szepen nyomonkovetheto legyen.
        /// </summary>
        private static void Test4()
        {
            Random r = new Random(DateTime.Now.Millisecond);

            var serverA = new RESTServer("localhost", "1234", "http");
            serverA.Start();
            var clientA = new RESTClient("http://localhost:1234/");

            Console.WriteLine("Press a key to start the test!");
            Console.ReadKey();
            Console.WriteLine();

            if (serverA.IsListening)
            {
                for (int i = 0; i < 20; i++)
                {
                    var tmp = new RESTRequest(resource: "/resource/a/random", method: (r.Next() % 2 == 0) ? HttpMethod.GET : HttpMethod.POST);
                    clientA.Execute(tmp);
                    Console.WriteLine("A {0} method sent. \t number of Post requests: {2} number of Get requests: {1}",tmp.Method,serverA.NOIGetReqCount, serverA.NOI64PostReqCount);
                    Thread.Sleep(r.Next(3, 7) * 100);
                }

                serverA.Stop();
            }
        }

        /// <summary>
        /// Másodperc alatt beérkező kérések szamanak monitorozasa.
        /// Koronkent elkuld 175 azonos hosszu payloaddal rendelkezo Post kerest egyre novekvo varakozassal
        /// ket keres kozott, igy egy szakaszosan csokkeno ratank lesz.
        /// </summary>
        private static void Test3()
        {
            Random r = new Random(DateTime.Now.Millisecond);

            var serverA = new RESTServer("localhost", "1234", "http");
            serverA.Start();
            var clientA = new RESTClient("http://localhost:1234/");

            Console.WriteLine("Press a key to start the test!");
            Console.ReadKey();
            Console.WriteLine();

            if (serverA.IsListening)
            {
                //korok szamat meghatarozo for ciklus
                for (int j = 1; j <= 4; j++)
                {
                    //minden korben egy 175 kerest tartalmazo osztalyt dolgozunk fel
                    Requests tester = new Requests(175, ProbeType.CONST);
                    for (int i = 0; i < tester.reqArray.Length; i++)
                    {
                        clientA.Execute(tester.reqArray[i]);
                        Thread.Sleep(j*15);
                    }
                    Console.WriteLine("{0}. iteration, rate: {1} 1/s",j,serverA.ROCPS64ReqPerSec);
                    Thread.Sleep(1000);
                }
                Console.WriteLine(   serverA._ROCPS64ReqPerSec.RawValue);

                serverA.Stop();
            }
        }

        /// <summary>
        /// Post keresek átlagos merete
        /// Haromszor eloallit egy novekvo, majd azt kovetoen csokkeno payload merettel rendelekezo request array-t
        /// majd azokat lefuttatja 400ms kesleltetessel, hogy performance monitoron latszon az eredmeny.
        /// 
        /// Vegen kiirja az osszes Post keres payloadjanak az atlagat
        /// </summary>
        private static void Test2()
        {
            Random r = new Random(DateTime.Now.Millisecond);

            var serverA = new RESTServer("localhost", "1234", "http");
            serverA.Start();
            var clientA = new RESTClient("http://localhost:1234/");

            Console.WriteLine("Press a key to start the test!");
            Console.ReadKey();
            Console.WriteLine();

            if (serverA.IsListening)
            {
                //harom sorozat lesz
                for (int j = 0; j < 3; j++)
                {
                    //tomb a novekvo payloaddal rendelkezo keresekhez
                    Requests tester = new Requests(j*4+4, ProbeType.INCRSTEP);
                    for (int i = 0; i < tester.reqArray.Length; i++)
                    {
                        clientA.Execute(tester.reqArray[i]);
                        Console.WriteLine("Számolt átlag az (i,i+1] intervallumon: {0}", serverA.AC64PostReq);
                        Thread.Sleep(400);
                    }
                    //uj tomb visszafele kiolvasva a csokkeno payloaddal torteno keresekhez
                    tester = new Requests(j * 4 + 4, ProbeType.INCRSTEP);
                    for (int i = tester.reqArray.Length - 1; i > 0; i--)
                    {
                        clientA.Execute(tester.reqArray[i]);
                        Console.WriteLine("Számolt átlag az (i,i+1] intervallumon: {0}", serverA.AC64PostReq);
                        Thread.Sleep(400);
                    }
                    Thread.Sleep(400);
                }
                //vegen kiirjuk az atlagot is
                Console.WriteLine("Az összes post kérés payloadjának átlaga eddig: {0}",serverA.AC64PostReqFullAverage());
                serverA.Stop();
            }
        }

        /// <summary>
        /// Tesztelése a sikeres es sikertelen requestek szamlaloinak.
        /// Elso fazisban megprobaltam kulonbozo helyekrol requestelni
        /// Masodik és harmadik fazisban a sikeres, majd a sikertelen requestek szamlalojat novelem
        /// Kozben varakozik 400ms-ot, hogy Performance Monitoron is lathato legyen
        /// </summary>
        private static void Test1()
        {
            Random r = new Random(DateTime.Now.Millisecond);

            var serverA = new RESTServer("localhost", "1234", "http");
            serverA.Start();
            var clientA = new RESTClient("http://localhost:1234/");

            Console.WriteLine("Press a key to start the test!");
            Console.ReadKey();
            Console.WriteLine();

            if (serverA.IsListening)
            {
                //elso fazis
                Console.WriteLine(clientA.Execute(new RESTRequest()).ElapsedTime + "ms" + " - html page as respond");
                Console.WriteLine("Successful requests A: {0}\t Not sucessful requests A: {1}", serverA.NOI64STran, serverA.NOI64NSTran);
                Thread.Sleep(400);
                Console.WriteLine(clientA.Execute(new RESTRequest("index.html")).ElapsedTime + "ms" + " - html page as respond");
                Console.WriteLine("Successful requests A: {0}\t Not sucessful requests A: {1}", serverA.NOI64STran, serverA.NOI64NSTran);
                Thread.Sleep(400);
                Console.WriteLine(clientA.Execute(new RESTRequest(resource: "/resource/a/random")).ElapsedTime + "ms" + " - server A responded");
                Console.WriteLine("Successful requests A: {0}\t Not sucessful requests A: {1}", serverA.NOI64STran, serverA.NOI64NSTran);
                Thread.Sleep(400);
                Console.WriteLine(clientA.Execute(new RESTRequest(resource: "/resource/b/random")).ElapsedTime + "ms" + " - not found reply");
                Console.WriteLine("Successful requests A: {0}\t Not sucessful requests A: {1}", serverA.NOI64STran, serverA.NOI64NSTran);

                //masodik fazis
                for (int i = 0; i < 10; i++)
                {
                    var req = new RESTRequest();
                    req.Method = HttpMethod.GET;
                    req.Resource = "/resource/a/" + r.Next(1000, 9999);
                    Console.WriteLine(clientA.Execute(req).ElapsedTime + "ms" + " - server A responded");
                    Console.WriteLine("Successful requests A: {0}\t Not sucessful requests A: {1}", serverA.NOI64STran, serverA.NOI64NSTran);
                    Thread.Sleep(400);
                }
                
                //harmadik fazis
                for (int i = 0; i < 10; i++)
                {
                    var req = new RESTRequest();
                    req.Method = HttpMethod.GET;
                    req.Resource = "/resource/not found" + r.Next(1000, 9999);
                    Console.WriteLine(clientA.Execute(new RESTRequest(resource: "/resource/b/random")).ElapsedTime + "ms" + " - not found reply");
                    Console.WriteLine("Successful requests A: {0}\t Not sucessful requests A: {1}", serverA.NOI64STran, serverA.NOI64NSTran);
                    Thread.Sleep(300);
                }
                serverA.Stop();
            }

        }
    }

    [RESTScope(BaseUrl = "http://localhost:1234/")]
    public sealed class ResourceA : RESTResource
    {
        [RESTRoute(Method = HttpMethod.GET, PathInfo = @"^/resource/a/[0-9.\-A-Za-z]*$")]
        public void MyGetResponder(HttpListenerContext context)
        {
            this.SendTextResponse(context, "Resource A");
        }
    }

    [RESTScope(BaseUrl = "http://localhost:1235/")]
    public sealed class ResourceB : RESTResource
    {
        [RESTRoute(Method = HttpMethod.GET, PathInfo = @"^/resource/b/[0-9.\-A-Za-z]+$")]
        public void MyGetResponder(HttpListenerContext context)
        {
            this.SendTextResponse(context, "Resource B");

        }

    }
}
