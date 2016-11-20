1. Teljesitmeny szamlalok letrehozasa a srcipt futtatasaval

	src/CreateGrapevinePerformanceCounters.ps1

2. Projekt forditasa ezzel a scripttel tortenik. Ebben At kell irni a CSC_ROOT-ot, ha a csc.exe valamiert 
nem az alapertelmezett helyen lenne, illetve a ROOT valtozonak a scc,test stb fileokat tartalmazo mappat atadni hiba eseten.

	src/GrapevineCompilerScript.ps1

Mappa tartalma:
	-Komplett solution
	-Fenti ket script
	-Tesztelesi dokumentacio
	-Teljesitmeny szamlalok es uml-t tartalmazo dokumentacio (PerfCntDesc.pdf)
	-Valtoztatasokat tartalmazo .diff fajlok