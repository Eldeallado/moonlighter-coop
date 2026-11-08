# Remote control for the QA copies: drive.ps1 host "walkto 100 200"   |   drive.ps1 guest info
param([Parameter(Mandatory)][ValidateSet("host","guest","guest2","guest3","guest4")]$Who, [Parameter(Mandatory)][string]$Cmd)
$port = @{ host = 7800; guest = 7801; guest2 = 7802; guest3 = 7803; guest4 = 7804 }[$Who]
$c = New-Object System.Net.Sockets.TcpClient("127.0.0.1", $port)
$s = $c.GetStream(); $w = New-Object System.IO.StreamWriter($s); $w.AutoFlush = $true; $r = New-Object System.IO.StreamReader($s)
foreach ($one in $Cmd -split ";;") { $w.WriteLine($one.Trim()); $r.ReadLine() }
$c.Close()
