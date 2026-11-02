# Banko Share

İki Windows bilgisayar arasında klavye ve fare paylaşımı. İlk geliştirme önizlemesi; günlük kullanım için henüz doğrulanmış değildir.

## İndir ve kur

[En güncel sürüm ve kurulum dosyası](https://github.com/berkbankoglu/banko-share/releases/latest)

İki bilgisayarda da **Setup.exe** ile biten dosyayı indirip çalıştırın. .NET ayrıca kurulmaz. Önceki ham ZIP sürümünden geçiyorsanız bir kez bu kurulum gerekir. Sonraki sürümleri uygulama otomatik alır.

## Güncellemeler

- Uygulama açılışta ve açıkken her 10 dakikada herkese açık GitHub Releases kaynağını kontrol eder; hesap veya anahtar gerekmez.
- Güncelleme panelinde kurulu sürüm, son kontrol, indirme yüzdesi ve kurulum durumu görünür.
- Yeni paket arka planda indirilir; Velopack paket bütünlüğünü doğrular. İndirme başarısızsa çalışan sürüm korunur ve sonraki kontrolde tekrar denenir.
- Bağlantı kurulurken, alıcı beklerken veya paylaşım oturumu açıkken kurulum yapılmaz. **Bağlantıyı durdur** sonrasında 15 saniyelik geri sayımla güncelleme uygulanıp uygulama yeniden açılır.
- **Güncellemeleri otomatik indir ve oturum yokken kur** seçeneği kapatılabilir. **Şimdi kontrol et** ve **Güncelle ve yeniden başlat** elle kullanım içindir.
- Uygulama kapalıyken arka plan servisi çalışmaz; kontrol sonraki açılışta yapılır.
- Tercihler `%LocalAppData%/BankoShare.Settings/updates.json` içinde, değiştirilen sürüm klasörünün dışında tutulur.
- Yayınlar geliştirme önizlemesidir; Windows kod imzalama sertifikası henüz yoktur. HTTPS GitHub kaynağı ve paket hash doğrulaması kullanılır.

Yeni sürüm yayımlama: [docs/RELEASING.md](docs/RELEASING.md).

## Mevcut kapsam

- C# / .NET 9 / WPF masaüstü arayüzü.
- Açıkça başlatılan alıcı modu; tek kontrolcü ve tek alıcı.
- TLS 1.2/1.3, SHA-256 sertifika sabitleme ve 256 bit rastgele oturum sırrı.
- Fare hareketi, beş düğme, dikey/yatay tekerlek ve fiziksel klavye tarama kodları.
- F12 ile yerel/uzak kontrol; alıcıdaki fiziksel F12 ile oturumu sonlandırma.
- Sınırlı olay kuyruğu, kalp atışı, üç saniyelik okuma zaman aşımı, bağlantı sonunda basılı girişleri bırakma.
- Hesap, bulut sunucusu, telemetri, tuş günlüğü veya otomatik başlangıç yok.

## Çalıştırma

Windows üzerinde .NET 9 SDK ile:

```powershell
dotnet build BankoShare.sln -c Release
dotnet run --project tests/BankoShare.Tests -c Release
dotnet run --project src/BankoShare.App -c Release
```

SDK gerektirmeyen x64 paketi:

```powershell
dotnet publish src/BankoShare.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Klasörün tamamını iki bilgisayara kopyalayın, `BankoShare.App.exe` dosyasını açın.

1. Kontrol edilecek bilgisayarda **Alıcıyı başlat** seçin.
2. Gösterilen yerel IPv4 adresini ve eşleştirme kodunu kontrolcü bilgisayara girin.
3. **Güvenli bağlan** seçin. Bağlantı kurulması tek başına giriş yakalamayı açmaz.
4. Tuşları ve fare düğmelerini bıraktıktan sonra **F12** ile uzak kontrolü açın. F12 ile geri dönün.
5. Alıcıdaki fiziksel F12 veya **Bağlantıyı durdur** oturumu kapatır. Yeniden bağlantı için yeni alıcı oturumu başlatın.

Alıcı TCP 24819 kullanır. Uygulama güvenlik duvarını değiştirmez. Windows bağlantıyı engellerse yalnızca güvendiğiniz özel ağda uygulamaya izin vermeniz gerekir. İnternete port yönlendirmeyin. Eşleştirme kodu oturum yetkisidir; paylaşmayın veya sorun kayıtlarına eklemeyin.

## Sınırlar

- Ekran kenarından otomatik geçiş, ekran düzenleyici, pano ve dosya aktarımı henüz yok.
- Yönetici uygulamaları, UAC, kilit ekranı, Ctrl+Alt+Del ve güvenli masaüstü desteklenmez. Normal kullanıcı olarak çalıştırın.
- Başlangıçta iki bilgisayarda aynı klavye düzenini kullanın. Medya tuşları, Pause/PrintScreen ve farklı düzen kombinasyonları henüz doğrulanmadı.
- F12 bağlantı boyunca uygulamaya ayrılır. Uygulama odağı kaybederse aktif gönderim kapanır.
- Göreli fare hareketi kullanılır; hız/ivme eşleştirmesi ve çoklu DPI davranışı iki cihazda test edilmelidir.
- Alıcı tüm IPv4 arayüzlerinde dinler; güvenlik duvarı kapsamını özel yerel ağla sınırlayın. Otomatik ağ keşfi yok.
- Süreç zorla öldürülürse veya sistem çökerse alıcının giriş bırakma kodu çalışamayabilir. Fiziksel klavye/fare erişimini koruyun.
- Kurulum ve otomatik güncelleme vardır; Windows Authenticode imzası henüz yoktur.

## Doğrulama

Test çalıştırıcısı gerçek loopback TLS bağlantılarıyla kimlik doğrulama, yanlış sertifika/sır reddi, paket doğrulaması, olay sırası, zaman aşımı, iptal ve kopmayı sınar. Güncelleme testleri oturum sırasında kurulumu engelleme, bekleme süresi, kullanıcı tercihi, indirme hatası, iptal ve eşzamanlı kontrolü kapsar. İşletim sistemine giriş göndermez. İki fiziksel bilgisayarda uçtan uca giriş testi henüz yapılmadı; test planı `docs/TESTING.md` içindedir.

## Sonraki adımlar

1. İki fiziksel bilgisayarda bağlantı ve giriş davranışını doğrulama.
2. Giriş motorunu ayrı mesaj döngüsüne taşıma ve DPI/ivme kalibrasyonu.
3. Ekran yerleşimi, kenardan geçiş ve yanlışlıkla geçiş engeli.
4. İsteğe bağlı metin panosu, cihazları hatırlama ve tepsi arayüzü.
5. Dosya aktarımı, imzalı dağıtım ve daha geniş uyumluluk testleri.

## Teknik referanslar

- [Windows SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)
- [KEYBDINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput)
- [.NET SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream)

Özgün uygulama; ShareMouse veya diğer projelerden kaynak kod alınmamıştır. Açık kaynak lisansı henüz seçilmedi.
