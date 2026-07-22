# taporcrash.vexorialabs.com deployment

Bu proje Unity WebGL çıktısı olarak Nginx üzerinden yayınlanır.

## 1. DNS

DNS sağlayıcısında aşağıdaki kaydı oluşturun:

```text
Type: A
Name: taporcrash
Value: 188.132.130.12
TTL: 300 (veya Auto)
```

## 2. WebGL çıktısı

Windows PowerShell'de proje kökünden:

```powershell
$unityArgs = @(
  '-batchmode', '-quit',
  '-projectPath', $PWD.Path,
  '-buildTarget', 'WebGL',
  '-executeMethod', 'WebGLBuild.Build',
  '-webGLBuildPath', 'Builds/WebGL',
  '-logFile', "$($PWD.Path)\Builds\webgl-build.log"
)

$process = Start-Process `
  -FilePath 'C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe' `
  -ArgumentList $unityArgs `
  -Wait -PassThru

if ($process.ExitCode -ne 0) {
  throw "Unity WebGL build failed with exit code $($process.ExitCode)."
}
```

## 3. Sunucu

Ubuntu/Debian ve Nginx varsayımıyla:

```bash
sudo apt update
sudo apt install -y nginx certbot python3-certbot-nginx
sudo mkdir -p /var/www/taporcrash
sudo chown -R "$USER":"$USER" /var/www/taporcrash
```

`Builds/WebGL/` içeriğini `/var/www/taporcrash/` dizinine, bu repodaki
`deploy/nginx/taporcrash.vexorialabs.com.conf` dosyasını da sunucudaki
`/etc/nginx/sites-available/taporcrash.vexorialabs.com` yoluna kopyalayın.

```bash
sudo ln -s /etc/nginx/sites-available/taporcrash.vexorialabs.com /etc/nginx/sites-enabled/taporcrash.vexorialabs.com
sudo nginx -t
sudo systemctl reload nginx
sudo certbot --nginx -d taporcrash.vexorialabs.com --redirect
```

Sertifika alınmadan önce DNS kaydının sunucuya yönlenmiş ve dışarıdan 80/443
portlarının açık olması gerekir.
