[CmdletBinding()]
param(
  [string]$StateFile='../../outputs/ui-review-20261107/full-flow-20261108.json',
  [switch]$LeaveForMobileReceipt
)
$ErrorActionPreference='Stop'
. ./demo-flow-helpers.ps1
$flowStatePath=(Resolve-Path $StateFile).Path
$flow=Get-Content $flowStatePath -Raw | ConvertFrom-Json -AsHashtable
$orderId=$flow.order.id
if(!$orderId){throw 'No demo order recorded'}
$admin=Get-DemoHeaders Admin
$keeper=Get-DemoHeaders WarehouseKeeper
$operator=Get-DemoHeaders Operator
$retailer=Get-DemoHeaders Retailer
function Save-Flow {$flow | ConvertTo-Json -Depth 40 | Set-Content $flowStatePath -Encoding utf8}
if(!$flow.earlyCollectionRejected){
  $payment=Invoke-DemoApi "payments/orders/$orderId" $operator
  $earlyHeader=@{Authorization=$operator.Authorization;'Idempotency-Key'="full-flow-early-$orderId"}
  try {Invoke-DemoApi "payments/orders/$orderId/collect" $earlyHeader Post @{collectionMethod='BankTransfer';expectedVersion=$payment.version;note='DEMO negative check before delivery'} | Out-Null; throw 'Early collection unexpectedly succeeded'}
  catch {if([int]$_.Exception.Response.StatusCode -ne 409){throw};$flow.earlyCollectionRejected=$true;Save-Flow;Write-Output 'PASS: collection rejected before receipt (409)'}
}
$allPicking=Invoke-DemoApi 'picking-lists' $keeper
$lists=@($allPicking).Where({$_.orderId -eq $orderId})
if(!$lists.Count){throw 'Demo picking list not accessible to keeper'}
foreach($list in $lists){
  $p=Invoke-DemoApi "picking-lists/$($list.pickingCode)" $keeper
  foreach($item in $p.items){
    if($item.remainingQuantity -le 0){continue}
    $labels=Invoke-DemoApi "qr-labels?batchId=$($item.batchId)" $keeper
    $label=@($labels).Where({$_.status -eq 'Available' -and $_.quantity -ge $item.remainingQuantity}) | Select-Object -First 1
    if(!$label){
      $genHeader=@{Authorization=$keeper.Authorization;'Idempotency-Key'="full-flow-label-$orderId-$($item.allocationId)"}
      $label=Invoke-DemoApi 'qr-labels' $genHeader Post @{batchId=$item.batchId;quantity=$item.remainingQuantity}
    }
    $scanHeader=@{Authorization=$keeper.Authorization;'Idempotency-Key'="full-flow-scan-$orderId-$($item.allocationId)"}
    $p=Invoke-DemoApi "picking-lists/$($p.pickingCode)/scans" $scanHeader Post @{qrCode=$label.qrCode;quantity=$item.remainingQuantity;expectedPickingVersion=$p.version;expectedLabelVersion=$label.version;splits=@(@{allocationRunAllocationId=$item.allocationId;quantity=$item.remainingQuantity})}
    Write-Output "Picked $($item.sku): $($item.remainingQuantity) cartons, $($label.qrCode)"
  }
  if($p.status -ne 'Completed'){$p=Invoke-DemoApi "picking-lists/$($p.pickingCode)/complete" $keeper Post @{expectedPickingVersion=$p.version;note='DEMO full flow: all labels picked'}}
  $flow.picking=$p;Save-Flow
}
$shipments=Invoke-DemoApi "shipments?orderId=$orderId" $admin
foreach($part in $shipments){
  $s=Invoke-DemoApi "shipments/$($part.id)" $operator
  if($s.status -eq 'Ready'){$s=Invoke-DemoApi "shipments/$($s.id)/issue" $keeper Post @{expectedVersion=$s.version}}
  if($s.status -eq 'Issued'){
    $drivers=Invoke-DemoApi 'deliveries/drivers' $operator
    $vehicles=Invoke-DemoApi 'deliveries/vehicles' $operator
    $driver=@($drivers).Where({$_.warehouseId -eq $s.warehouseId -and $_.isActive}) | Select-Object -First 1
    $vehicle=@($vehicles).Where({$_.warehouseId -eq $s.warehouseId -and $_.isActive -and $_.capacity -ge $s.totalQuantity}) | Select-Object -First 1
    if(!$driver.id -or !$vehicle.id){throw 'Demo driver or vehicle missing'}
    if(!$s.driverId){$s=Invoke-DemoApi "shipments/$($s.id)/assignment" $operator Patch @{driverId=$driver.id;vehicleId=$vehicle.id;expectedVersion=$s.version}}
    $s=Invoke-DemoApi "shipments/$($s.id)/dispatch" $operator Post @{expectedVersion=$s.version}
  }
  if($s.status -eq 'Delivering' -and !$LeaveForMobileReceipt){
    $picked=Invoke-DemoApi "picking-lists/$($s.pickingCode)" $keeper
    $receiptHeader=@{Authorization=$retailer.Authorization;'Idempotency-Key'="full-flow-receipt-$($s.id)"}
    $lines=@($s.lines | ForEach-Object {@{orderLineId=$_.orderLineId;batchId=$_.batchId;receivedQuantity=$_.quantity;damagedQuantity=0}})
    $s=Invoke-DemoApi "shipments/$($s.id)/receipt" $receiptHeader Post @{expectedVersion=$s.version;qrCodes=@($picked.scans | ForEach-Object {$_.qrCode});lines=$lines;note='DEMO full flow: retailer received every carton intact'}
  }
  if($s.status -ne 'Received' -and !($LeaveForMobileReceipt -and $s.status -eq 'Delivering')){throw "Unexpected shipment state $($s.status)"}
  $flow.shipment=$s;Save-Flow
  Write-Output "Shipment $($s.shipmentCode): $($s.status), $($s.receivedQuantity) cartons"
}
$payment=Invoke-DemoApi "payments/orders/$orderId" $operator
$flow.receivableBeforeCollection=$payment;Save-Flow
$payment | Select-Object orderCode,status,amount,outstandingAmount,creditTermDays,deliveredDate,dueDate,version | Format-List
