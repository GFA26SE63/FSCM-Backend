[CmdletBinding()]
param(
    [Parameter(Mandatory)][uri] $ApiBaseUrl,
    [Parameter(Mandatory)][System.Management.Automation.PSCredential] $Sales,
    [Parameter(Mandatory)][System.Management.Automation.PSCredential] $Retailer,
    [Parameter(Mandatory)][System.Management.Automation.PSCredential] $WarehouseKeeper,
    [Parameter(Mandatory)][guid] $RetailerId,
    [Parameter(Mandatory)][guid] $ProductId,
    [Parameter(Mandatory)][guid] $ClientOrderKey,
    [ValidateRange(1, 1000)][int] $Quantity = 12
)

$ErrorActionPreference = 'Stop'
$api = $ApiBaseUrl.AbsoluteUri.TrimEnd('/')

function Invoke-FixtureApi($Path, $Headers, $Method = 'Get', $Body = $null) {
    $request = @{ Uri = "$api/$Path"; Method = $Method; Headers = $Headers }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json'
        $request.Body = ConvertTo-Json -InputObject $Body -Depth 8
    }
    Invoke-RestMethod @request
}

function Get-FixtureAuthorization([System.Management.Automation.PSCredential] $Credential) {
    $session = Invoke-FixtureApi 'auth/login' @{} Post @{
        identifier = $Credential.UserName
        password = $Credential.GetNetworkCredential().Password
    }
    if (!$session.accessToken) { throw 'Login did not return an access token.' }
    @{ Authorization = "Bearer $($session.accessToken)" }
}

$salesHeaders = Get-FixtureAuthorization $Sales
$retailerHeaders = Get-FixtureAuthorization $Retailer
$warehouseHeaders = Get-FixtureAuthorization $WarehouseKeeper

$order = Invoke-FixtureApi 'orders' $salesHeaders Post @{
    clientOrderKey = $ClientOrderKey
    retailerId = $RetailerId
    paymentMethod = 'Credit'
    note = "DEMO-FIXTURE $ClientOrderKey"
    lines = @(@{ productId = $ProductId; quantity = $Quantity })
}
Write-Host "Demo order: $($order.orderCode) ($($order.id))"

$quotes = Invoke-FixtureApi "quotes/orders/$($order.id)" $retailerHeaders
$quote = $quotes | Sort-Object -Property version -Descending | Select-Object -First 1
if (!$quote.id) {
    throw 'The order has no quote. Check stock, retailer credit, and distance for this order.'
}
if (!$quote.purchaseOrderId) {
    $quote = Invoke-FixtureApi "quotes/$($quote.id)/accept" $retailerHeaders Post @{
        expectedVersion = $quote.version
        paymentMethod = 'Credit'
    }
}

$picking = Invoke-FixtureApi 'picking-lists' $warehouseHeaders |
    Where-Object { $_.orderId -eq $order.id }
if (!$picking) { throw 'No picking list is visible to the Warehouse Keeper for this order.' }

[pscustomobject]@{
    OrderId = $order.id
    OrderCode = $order.orderCode
    PurchaseOrderId = $quote.purchaseOrderId
    PickingLists = @($picking)
}
