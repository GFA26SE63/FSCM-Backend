[CmdletBinding()]
param(
    [Parameter(Mandatory)][uri] $ApiBaseUrl,
    [Parameter(Mandatory)][hashtable] $AdminHeaders,
    [datetime] $BusinessDate = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'SE Asia Standard Time').Date
)

# Demo-only inventory received through the real workflow. Repeating the same date does not add stock twice.
$ErrorActionPreference = 'Stop'
$api = $ApiBaseUrl.AbsoluteUri.TrimEnd('/')
function Invoke-SeedApi($Path, $Method = 'Get', $Body = $null, $Headers = $AdminHeaders) {
    $request = @{ Uri = "$api/$Path"; Method = $Method; Headers = $Headers }
    if ($null -ne $Body) { $request.ContentType = 'application/json'; $request.Body = ConvertTo-Json -InputObject $Body -Depth 10 }
    Invoke-RestMethod @request
}
$products = (Invoke-SeedApi 'products?page=1&pageSize=100').items
$warehouses = Invoke-SeedApi 'warehouses'
$specs = @(
    @{ Sku = 'SKU001'; Life = 7; Remaining = @(1,3,6); Quantities = @(20,40,80) },
    @{ Sku = 'SKU002'; Life = 14; Remaining = @(2,7,12); Quantities = @(20,50,90) },
    @{ Sku = 'SKU005'; Life = 30; Remaining = @(4,14,26); Quantities = @(30,60,100) }
)
$result = @()
foreach ($warehouseCode in @('WH-TD','WH-Q7','WH-HCM-01')) {
    $warehouse = $warehouses | Where-Object code -eq $warehouseCode
    if (!$warehouse.isActive) { throw "Missing active warehouse $warehouseCode" }
    $lines = @()
    foreach ($spec in $specs) {
        $product = $products | Where-Object sku -eq $spec.Sku
        if ($product.status -ne 'Published') { throw "Missing published product $($spec.Sku)" }
        for ($i = 0; $i -lt 3; $i++) {
            $expiry = $BusinessDate.AddDays($spec.Remaining[$i])
            $lines += @{
                productId = $product.id; sourceLotCode = "DEMO-OPT-$($BusinessDate.ToString('yyyyMMdd'))-$($spec.Sku)-$i"
                quantity = $spec.Quantities[$i]
                productionDate = $expiry.AddDays(-$spec.Life).ToString('yyyy-MM-dd')
                expirationDate = $expiry.ToString('yyyy-MM-dd')
            }
        }
    }
    $headers = $AdminHeaders.Clone()
    $headers['Idempotency-Key'] = "demo-optimization-v1-$warehouseCode-$($BusinessDate.ToString('yyyyMMdd'))"
    $receipt = Invoke-SeedApi 'goods-receipts' Post @{warehouseId=$warehouse.id; lines=$lines} $headers
    $approved = Invoke-SeedApi "goods-receipts/$($receipt.id)/approve" Post
    $result += [pscustomobject]@{ Warehouse=$warehouseCode; ReceiptId=$receipt.id; ReceiptCode=$approved.receiptCode; Status=$approved.status; Lines=$approved.lines }
}
$result
