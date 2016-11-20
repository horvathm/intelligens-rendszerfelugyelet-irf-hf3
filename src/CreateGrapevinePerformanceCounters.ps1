param(    
    [switch] $Force
)

$CATEGORY_NAME = "IRF_Rest"

$identity=[System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal=new-object System.Security.Principal.WindowsPrincipal($identity)
$adminRole=[System.Security.Principal.WindowsBuiltInRole]::Administrator

if (! $principal.IsInRole($adminRole))
{
    echo "This script should be run as administrator!"
    return
}

$exists = [System.Diagnostics.PerformanceCounterCategory]::Exists($CATEGORY_NAME)
if ($exists -and ! $Force)
{
    echo "Category $CATEGORY_NAME exists, call the script with the -Force parameter to delete it before creating!"
    return
}

if ($Force -and $exists)
{   
    echo "Deleting $CATEGORY_NAME category"          
    [System.Diagnostics.PerformanceCounterCategory]::Delete($CATEGORY_NAME);
}

echo "Creating $CATEGORY_NAME category with counter"

$CounterDatas = New-Object System.Diagnostics.CounterCreationDataCollection;

$cdCounter1 = new-object System.Diagnostics.CounterCreationData;
$cdCounter1.CounterName = "NOI64NSTran";
$cdCounter1.CounterHelp = "Number of items 64 bit counter for not successful tranfers"
$cdCounter1.CounterType = [System.Diagnostics.PerformanceCounterType]::NumberOfItems64

$cdCounter2 = new-object System.Diagnostics.CounterCreationData
$cdCounter2.CounterName = "NOI64STran"
$cdCounter2.CounterHelp = "Number of items 64 bit counter for successful tranfers"
$cdCounter2.CounterType = [System.Diagnostics.PerformanceCounterType]::NumberOfItems64

$cdCounter3 = new-object System.Diagnostics.CounterCreationData
$cdCounter3.CounterName = "AC64PostReq"
$cdCounter3.CounterHelp = "Avg"
$cdCounter3.CounterType = [System.Diagnostics.PerformanceCounterType]::AverageCount64

$cdCounter4 = new-object System.Diagnostics.CounterCreationData
$cdCounter4.CounterName = "AC64PostReqBase"
$cdCounter4.CounterHelp = "Base"
$cdCounter4.CounterType = [System.Diagnostics.PerformanceCounterType]::AverageBase

$cdCounter5 = new-object System.Diagnostics.CounterCreationData
$cdCounter5.CounterName = "ROCPS64ReqPerSec"
$cdCounter5.CounterHelp = "Rate"
$cdCounter5.CounterType = [System.Diagnostics.PerformanceCounterType]::RateOfCountsPerSecond32

$cdCounter6 = new-object System.Diagnostics.CounterCreationData
$cdCounter6.CounterName = "NOI64PostReqCount"
$cdCounter6.CounterHelp = "Counts"
$cdCounter6.CounterType = [System.Diagnostics.PerformanceCounterType]::NumberOfItems64

$cdCounter7 = new-object System.Diagnostics.CounterCreationData
$cdCounter7.CounterName = "NOI64GetReqCount"
$cdCounter7.CounterHelp = "Counts too"
$cdCounter7.CounterType = [System.Diagnostics.PerformanceCounterType]::NumberOfItems64

$CounterDatas.Add($cdCounter1) > $null
$CounterDatas.Add($cdCounter2) > $null
$CounterDatas.Add($cdCounter3) > $null
$CounterDatas.Add($cdCounter4) > $null
$CounterDatas.Add($cdCounter5) > $null
$CounterDatas.Add($cdCounter6) > $null
$CounterDatas.Add($cdCounter7) > $null

[System.Diagnostics.PerformanceCounterCategory]::Create($CATEGORY_NAME, "Performance counters for the GrapevineExample sample application", [System.Diagnostics.PerformanceCounterCategoryType]::MultiInstance, $CounterDatas)


