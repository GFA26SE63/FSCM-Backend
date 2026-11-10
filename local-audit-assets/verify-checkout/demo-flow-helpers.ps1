$ErrorActionPreference='Stop'
$api='http://localhost:5246/api/v1'
function Invoke-DemoApi($Path,$Headers,$Method='Get',$Body=$null) {
    $request=@{Uri="$api/$Path";Headers=$Headers;Method=$Method}
    if($null -ne $Body){$request.ContentType='application/json';$request.Body=$Body|ConvertTo-Json -Depth 12}
    Invoke-RestMethod @request
}
function Get-DemoHeaders($Role) {
    if($Role -eq 'Admin'){
        $config=Get-Content -Raw ../../Secrets_Folder/FSCM-Backend/src/FSCM.API/appsettings.json|ConvertFrom-Json
        $builder=[System.Data.Common.DbConnectionStringBuilder]::new()
        $builder.set_ConnectionString($config.ConnectionStrings.DefaultConnection)
        $credential=@{identifier='admin@fscm.local';password=$builder.get_Item('password')}
    }else{
        $secure=Get-Content "demo-fixture-$Role.credential"|ConvertTo-SecureString
        $credential=[System.Net.NetworkCredential]::new('',$secure).Password|ConvertFrom-Json
    }
    $session=Invoke-DemoApi 'auth/login' @{} Post $credential
    @{Authorization="Bearer $($session.accessToken)"}
}
