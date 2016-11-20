using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using Grapevine.Util;
using System.Diagnostics;
using System.Collections;

namespace Grapevine.Server
{
    /// <summary>
    /// Delegate for pre and post starting and stopping of RESTServer
    /// </summary>
    public delegate void ToggleServerHandler();

    public class RESTServer : Responder, IDisposable
    {
        #region Instance Variables   
        private readonly Dictionary<string, RESTResource> _resources;
        private readonly List<MethodInfo> _routes;

        private readonly Thread _listenerThread;
        private readonly Thread[] _workers;

        private readonly HttpListener _listener = new HttpListener();
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private Queue<HttpListenerContext> _queue = new Queue<HttpListenerContext>();

        //LockObject szálkezeléshez tartozó lock objektum
        private object _lockObject = new object();

        //ArrayList, amiben a számlálók által vett mintákat tárolunk.
        private ArrayList _sampleListAvgPost = new ArrayList();
        private ArrayList _sampleListReqPerSec = new ArrayList();

        #endregion

        #region Constructors

        public RESTServer(string host = "localhost", string port = "1234", string protocol = "http", string dirindex = "index.html", string webroot = null, int maxthreads = 5)
        {
            this.IsListening = false;
            this.DirIndex = dirindex;

            this.Host = host;
            this.Port = port;
            this.Protocol = protocol;
            this.MaxThreads = maxthreads;

            this.WebRoot = webroot;
            if (object.ReferenceEquals(this.WebRoot, null))
            {
                this.WebRoot = Path.Combine(Path.GetDirectoryName(Assembly.GetCallingAssembly().Location), "webroot");
            }

            this._resources = this.LoadRestResources();
            this._routes = this.LoadRestRoutes();

            this._workers = new Thread[this.MaxThreads];
            this._listenerThread = new Thread(this.HandleRequests);

            //Szamlalok definicioja: IRF_Rest kategoria, CounterName, instanceName, ReadOnly 
            this._NOI64NSTran = new PerformanceCounter(Counters.CategoryName, "NOI64NSTran", this.Port, false);
            this._NOI64STran = new PerformanceCounter(Counters.CategoryName, "NOI64STran", this.Port, false);
            this._AC64PostReq = new PerformanceCounter(Counters.CategoryName, "AC64PostReq", this.Port, false);
            this._AC64PostReqBase = new PerformanceCounter(Counters.CategoryName, "AC64PostReqBase", this.Port, false);
            this._ROCPS64ReqPerSec = new PerformanceCounter(Counters.CategoryName, "ROCPS64ReqPerSec", this.Port, false);
            this._NOI64PostReqCount = new PerformanceCounter(Counters.CategoryName, "NOI64PostReqCount", this.Port, false);
            this._NOI64GetReqCount = new PerformanceCounter(Counters.CategoryName, "NOI64GetReqCount", this.Port, false);
        }

        public RESTServer(Config config) : this(host: config.Host, port: config.Port, protocol: config.Protocol, dirindex: config.DirIndex, webroot: config.WebRoot, maxthreads: config.MaxThreads) { }

        private List<MethodInfo> LoadRestRoutes()
        {
            List<MethodInfo> routes = new List<MethodInfo>();

            foreach (KeyValuePair<string, RESTResource> pair in this._resources)
            {
                pair.Value.Server = this;
                var methods = pair.Value.GetType().GetMethods().Where(mi => !mi.IsStatic && mi.GetCustomAttributes(true).Any(attr => attr is RESTRoute)).ToList<MethodInfo>();
                routes.AddRange(methods);
            }

            return routes;
        }

        private Dictionary<string, RESTResource> LoadRestResources()
        {
            Dictionary<string, RESTResource> resources = new Dictionary<string, RESTResource>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if ((assembly.GlobalAssemblyCache) || (Enum.IsDefined(typeof(AssembliesToIgnore), assembly.GetName().Name))) continue;

                foreach (Type type in assembly.GetTypes())
                {
                    if ((!type.IsAbstract) && (type.IsSubclassOf(typeof(RESTResource))))
                    {
                        if (type.IsSealed)
                        {
                            if (type.GetCustomAttributes(true).Any(attr => attr is RESTScope))
                            {
                                var scopes = type.GetCustomAttributes(typeof(RESTScope), true);
                                foreach (RESTScope scope in scopes)
                                {
                                    if ((scope.BaseUrl.Equals(this.BaseUrl)) || (scope.BaseUrl.Equals("*")))
                                    {
                                        resources.Add(type.Name, Activator.CreateInstance(type) as RESTResource);
                                    }
                                }
                            }
                            else
                            {
                                resources.Add(type.Name, Activator.CreateInstance(type) as RESTResource);
                            }
                        }
                        else
                        {
                            throw new ArgumentException(type.Name + " inherits from RESTResource but is not sealed!");
                        }
                    }
                }
            }

            return resources;
        }

        private bool VerifyWebRoot(string webroot)
        {
            if (!Object.ReferenceEquals(webroot, null))
            {
                try
                {
                    if (!Directory.Exists(webroot))
                    {
                        Directory.CreateDirectory(webroot);
                    }
                    return true;
                }
                catch (Exception e)
                {
                    EventLogger.Log(e);
                }
            }
            return false;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Delegate used to execute custom code before attempting server start
        /// </summary>
        public ToggleServerHandler OnBeforeStart { get; set; }

        /// <summary>
        /// Delegate used to execute custom code after successful server start
        /// </summary>
        public ToggleServerHandler OnAfterStart { get; set; }

        /// <summary>
        /// Delegate used to execute custom code before attempting server stop
        /// </summary>
        public ToggleServerHandler OnBeforeStop { get; set; }

        /// <summary>
        /// Delegate used to execute custom code after successful server stop
        /// </summary>
        public ToggleServerHandler OnAfterStop { get; set; }

        /// <summary>
        /// Delegate used to execute custom code after successful server start; synonym for OnAfterStart
        /// </summary>
        public ToggleServerHandler OnStart
        {
            get
            {
                return this.OnAfterStart;
            }
            set
            {
                this.OnAfterStart = value;
            }
        }

        /// <summary>
        /// Delegate used to execute custom code after successful server stop; synonym for OnAfterStop
        /// </summary>
        public ToggleServerHandler OnStop
        {
            get
            {
                return this.OnAfterStop;
            }
            set
            {
                this.OnAfterStop = value;
            }
        }

        /// <summary>
        /// Returns true if the server is currently listening for incoming traffic
        /// </summary>
        public bool IsListening { get; private set; }

        /// <summary>
        /// Default file to return when a directory is requested without a file name
        /// </summary>
        public string DirIndex { get; set; }

        /// <summary>
        /// Specifies the top-level directory containing website content; value will not be set unless the directory exists or can be created
        /// </summary>
        public string WebRoot
        {
            get
            {
                return this._webroot;
            }
            set
            {
                if (VerifyWebRoot(value))
                {
                    this._webroot = value;
                }
            }
        }
        private string _webroot;

        /// <summary>
        /// <para>The host name used to create the HttpListener prefix</para>
        /// <para>&#160;</para>
        /// <para>Use "*" to indicate that the HttpListener accepts requests sent to the port if the requested URI does not match any other prefix. Similarly, to specify that the HttpListener accepts all requests sent to a port, replace the host element with the "+" character.</para>
        /// </summary>
        public string Host
        {
            get
            {
                return this._host;
            }
            set
            {
                if (!this.IsListening)
                {
                    this._host = value;
                }
                else
                {
                    var e = new ServerStateException("Attempted to modify Host property after server start.");
                    EventLogger.Log(e);
                    throw e;
                }
            }
        }
        private string _host;

        /// <summary>
        /// The port number (as a string) used to create the HttpListener prefix
        /// </summary>
        public string Port
        {
            get
            {
                return this._port;
            }
            set
            {
                if (!this.IsListening)
                {
                    this._port = value;
                }
                else
                {
                    var e = new ServerStateException("Attempted to modify Port property after server start.");
                    EventLogger.Log(e);
                    throw e;
                }
            }
        }
        private string _port;

        /// <summary>
        /// Returns the prefix created by combining the Protocol, Host and Port properties
        /// </summary>
        public string BaseUrl
        {
            get
            {
                return String.Format("{0}://{1}:{2}/", this.Protocol, this.Host, this.Port);
            }
        }

        /// <summary>
        /// The number of threads that will be started to respond to queued requests
        /// </summary>
        public int MaxThreads
        {
            get
            {
                return this._maxt;
            }
            set
            {
                if (!this.IsListening)
                {
                    if (value >= 1)
                    {
                        this._maxt = value;
                    }
                    else
                    {
                        var e = new ServerStateException("MaxThreads cannot be set to a value less than 1");
                        EventLogger.Log(e);
                        throw e;
                    }
                }
                else
                {
                    var e = new ServerStateException("Attempted to modify MaxThreads property after server start.");
                    EventLogger.Log(e);
                    throw e;
                }
            }
        }
        private int _maxt;

        /// <summary>
        /// <para>The URI scheme (or protocol) to be used in creating the HttpListener prefix; ex. "http" or "https"</para>
        /// <para>&#160;</para>
        /// <para>Note that if you create an HttpListener using https, you must select a Server Certificate for that listener. See the MSDN documentation on the HttpListener class for more information.</para>
        /// </summary>
        public string Protocol
        {
            get
            {
                return _protocol;
            }
            set
            {
                if (!this.IsListening)
                {
                    this._protocol = value;
                }
                else
                {
                    var e = new ServerStateException("Attempted to modify Protocol property after server start.");
                    EventLogger.Log(e);
                    throw e;
                }
            }
        }
        private string _protocol;

        /// <summary>
        /// Publikus property. Visszaadja a private PC erteket. Privat lathatosagu beallitasi lehetoseget nem hasznaltam ki.
        /// </summary>
        public long NOI64NSTran
        {
            get { return _NOI64NSTran.RawValue; }
            private set { lock (_lockObject) { _NOI64NSTran.RawValue = value; } }
        }
        private PerformanceCounter _NOI64NSTran;

        /// <summary>
        /// Publikus property. Visszaadja a private PC erteket.Privat lathatosagu beallitasi lehetoseget nem hasznaltam ki.
        /// </summary>
        public long NOI64STran
        {
            get { return _NOI64STran.RawValue; }
            private set { lock (_lockObject) { _NOI64STran.RawValue = value; } }
        }
        private PerformanceCounter _NOI64STran;

        /// <summary>
        /// Publikus property a _sampleListAvgPost ArrayList utolso ket bejegyzese alapjan
        /// kiszamolja az atlagot a beepitett szamolasi mechanizmus szerint.
        /// </summary>
        public double AC64PostReq
        {
            get
            {
                int i = _sampleListAvgPost.Count;
                if (i == 0)
                {
                    return 0;
                }
                else if (i == 1)
                {
                    return AC64PostReqFullAverage();
                }
                else
                {
                    return (double)CounterSampleCalculator.ComputeCounterValue((CounterSample)_sampleListAvgPost[i - 2], (CounterSample)_sampleListAvgPost[i - 1]);
                }
            }
        }
        private PerformanceCounter _AC64PostReq;
        private PerformanceCounter _AC64PostReqBase;

        /// <summary>
        /// Publikus property. Visszaadja a privat elerhetosegu Post requesteket szamolo PC erteket.
        /// </summary>
        public long NOI64PostReqCount
        {
            get
            {
                return _NOI64PostReqCount.RawValue;
            }
        }
        private PerformanceCounter _NOI64PostReqCount;

        /// <summary>
        /// Publikus property. Visszaadja a privat elerhetosegu Get requesteket szamolo PC erteket.
        /// </summary>
        public long NOIGetReqCount
        {
            get
            {
                return _NOI64GetReqCount.RawValue;
            }
        }
        private PerformanceCounter _NOI64GetReqCount;

        /// <summary>
        /// Publikus property. Lekerdezheto vele a _sampleListReqPerSec ArrayList utolso ket bejegyzese,
        /// ami alapjan kiszamolja a az egy masodperc alatt vegrehajtott muveleteket. 
        /// </summary>
        public double ROCPS64ReqPerSec
        {
            get
            {
                int i = _sampleListReqPerSec.Count;
                if (i <= 1)
                {
                    return 0;
                }
                else
                {
                    return CounterSampleCalculator.ComputeCounterValue((CounterSample)_sampleListReqPerSec[i - 2], (CounterSample)_sampleListReqPerSec[i - 1]);
                }
            }
        }
        public PerformanceCounter _ROCPS64ReqPerSec;

        #endregion

        #region Public Methods

        /// <summary>
        /// Attempts to start the server; executes delegates for OnBeforeStart and OnAfterStart
        /// </summary>
        public void Start()
        {
            if (!this.IsListening)
            {
                try
                {   
                    //Minden indításkor init valtozok
                    _NOI64NSTran.RawValue = 0;
                    _NOI64STran.RawValue = 0;
                    _AC64PostReq.RawValue = 0;
                    _AC64PostReqBase.RawValue = 0;
                    _ROCPS64ReqPerSec.RawValue = 0;
                    _NOI64GetReqCount.RawValue = 0;
                    _NOI64PostReqCount.RawValue = 0;
                    

                    this.FireDelegate(this.OnBeforeStart);

                    this.IsListening = true;
                    this._listener.Prefixes.Add(this.BaseUrl);

                    this._listener.Start();
                    this._listenerThread.Start();

                    for (int i = 0; i < _workers.Length; i++)
                    {
                        _workers[i] = new Thread(Worker);
                        _workers[i].Start();
                    }

                    this.FireDelegate(this.OnAfterStart);
                }
                catch (Exception e)
                {
                    this.IsListening = false;
                    EventLogger.Log(e);
                    throw;
                }
            }
        }

        /// <summary>
        /// Attempts to stop the server; executes delegates for OnBeforeStop and OnAfterStop
        /// </summary>
        public void Stop()
        {
            try
            {
                this.FireDelegate(this.OnBeforeStop);

                this._stop.Set();
                if (!object.ReferenceEquals(this._workers, null))
                {
                    foreach (Thread worker in this._workers)
                    {
                        worker.Join();
                    }
                }
                this._listener.Stop();
                this.IsListening = false;

                this.FireDelegate(this.OnAfterStop);
            }
            catch (Exception e)
            {
                EventLogger.Log(e);
                throw;
            }
        }

        /// <summary>
        /// Implementation of the IDisposable interface; explicitly calls the Stop() method
        /// </summary>
        public void Dispose()
        {
            this.Stop();
        }

        /// <summary>
        /// Publikus metodus a Post keresek paykoad hossz atlaganak lekeresehez adott intervallumon belul. 
        /// </summary>
        /// <param name="from">intervallum kezdete</param><param name="to">intervallum vege</param>
        public double AC64PostReqInterval(int from, int to)
        {

            if (_sampleListAvgPost.Count == 0)
            {
                return 0;
            }
            else if (_sampleListAvgPost.Count == 1)
            {
                return _AC64PostReq.RawValue;
            }
            else
            {
                if (from < 0) from = 0;
                if (to >= _sampleListAvgPost.Count) to = _sampleListAvgPost.Count - 1;
                return (double)CounterSampleCalculator.ComputeCounterValue((CounterSample)_sampleListAvgPost[from], (CounterSample)_sampleListAvgPost[to]);
            }
        }

        /// <summary>
        /// Publikus metodus. Az eddigi osszes Post keres payload hosszainak az atlaga
        /// </summary>
        /// <returns>Osszes Post keres payloadjainak az atlaga</returns>
        public double AC64PostReqFullAverage()
        {
            return _AC64PostReq.RawValue / _AC64PostReqBase.RawValue;
        }

        #endregion

        #region Private Threading Methods

        private void HandleRequests()
        {
            while (this.IsListening)
            {
                try
                {
                    var context = this._listener.GetContext();
                    //bejovo request eseten, ha az Get volt, akkor a _NOI64GetReqCount szamlalot inkrementaljuk
                    if (context.Request.HttpMethod.Equals("GET"))
                    {
                        _NOI64GetReqCount.Increment();
                    }
                    //ha a bejovo request Post volt, akkor a _NOI64PostReqCount szamlalot inkrementaljuk
                    if (context.Request.HttpMethod.Equals("POST"))
                    {
                        //ha a request Post volt, akkor meg a keresek payloadjanak atlagos merete is kell
                        var length = (int)context.Request.ContentLength64;
                        _NOI64PostReqCount.Increment();
                        if(length !=-1)
                            _AC64PostReq.IncrementBy(length);
                        _AC64PostReqBase.Increment();
                        _sampleListAvgPost.Add(_AC64PostReq.NextSample());
                    }
                    this.QueueRequest(context);
                }
                catch (Exception e)
                {
                    EventLogger.Log(e);
                }
            }
        }

        private void QueueRequest(HttpListenerContext context)
        {
            try
            {
                lock (this._queue)
                {
                    this._queue.Enqueue(context);
                    this._ready.Set();
                }
                //Miutan varakozasi sorba bekerult a keres inkrementaljuk masodpercenkent beerkezo keresek szamlalojat
                //  ennel a pontnal tekintettem a kerest beerkezettnek
                this._ROCPS64ReqPerSec.Increment();
                this._sampleListReqPerSec.Add(_ROCPS64ReqPerSec.NextSample());
            }
            catch (Exception e)
            {
                EventLogger.Log(e);
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            var notfound  = true;
            Exception scripterr = null;

            try
            {
                var rpath = (context.Request.RawUrl.Split('?'))[0];
                var route = this._routes.FirstOrDefault(mi => mi.GetCustomAttributes(true).Any(attr => rpath.Matches(((RESTRoute)attr).PathInfo) && context.Request.HttpMethod.ToUpper().Equals(((RESTRoute)attr).Method.ToString())));
                if (!object.ReferenceEquals(route, null))
                {
                    //Sikeres requestek szamlalojat noveljuk
                    _NOI64STran.Increment();
                    route.Invoke(this._resources[route.ReflectedType.Name], new object[] { context });
                    notfound = false;
                }
                else if ((context.Request.HttpMethod.ToUpper().Equals("GET")) && (!object.ReferenceEquals(this.WebRoot, null)))
                {
                    var filename = this.GetFilePath(context.Request.RawUrl);
                    if (!object.ReferenceEquals(filename, null))
                    {   //Sikeres requestek szamlalojat noveljuk
                        _NOI64STran.Increment();
                        this.SendFileResponse(context, filename);
                        notfound = false;
                    }
                }
            }
            catch (Exception e)
            {
                scripterr = e;
                EventLogger.Log(e);
            }
            finally
            {
                if (notfound)
                {
                    if (object.ReferenceEquals(scripterr, null))
                    {
                        //ha nem talaltuk meg es scripterr == NULL, akkor noveljuk a sikertelen requestek szamlalojat
                        _NOI64NSTran.Increment();
                        this.NotFound(context);
                    }
                    else
                    {
                        //ha scripterr != NULL, akkor noveljuk a sikertelen requestek szamlalojat
                        _NOI64NSTran.Increment();
                        this.InternalServerError(context, scripterr);
                    }
                }
            }
        }

        private void Worker()
        {
            WaitHandle[] wait = new[] { this._ready, this._stop };
            while (0 == WaitHandle.WaitAny(wait))
            {
                try
                {
                    HttpListenerContext context;
                    lock (this._queue)
                    {
                        if (this._queue.Count > 0)
                        {
                            context = this._queue.Dequeue();
                        }
                        else
                        {
                            this._ready.Reset();
                            continue;
                        }
                    }

                    this.ProcessRequest(context);
                }
                catch (Exception e)
                {
                    EventLogger.Log(e);
                }
            }
        }

        #endregion

        #region Private Utility Methods

        private string GetFilePath(string rawurl)
        {
            var filename = ((rawurl.IndexOf("?") > -1) ? rawurl.Split('?') : rawurl.Split('#'))[0].Replace('/', Path.DirectorySeparatorChar).Substring(1);
            var path = Path.Combine(this.WebRoot, filename);

            if (File.Exists(path))
            {
                return path;
            }
            else if (Directory.Exists(path))
            {
                path = Path.Combine(path, this.DirIndex);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private void FireDelegate(ToggleServerHandler method)
        {
            if (!object.ReferenceEquals(method, null))
            {
                try
                {
                    method();
                }
                catch (Exception e)
                {
                    EventLogger.Log(e);
                }
            }
        }

        #endregion
    }

    enum AssembliesToIgnore { vshost32, Grapevine, GrapevinePlus }
}