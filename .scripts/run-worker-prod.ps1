$workerDir = "C:\Projects\Tubester\.artifacts\worker"
$workerExe = Join-Path $workerDir "YouTubester.Worker.exe"

$tunnel = Start-Process "ssh.exe" -ArgumentList "-N", "tubester-db-tunnel" -WindowStyle Hidden -PassThru

Start-Sleep -Seconds 2

Start-Process $workerExe -WorkingDirectory $workerDir