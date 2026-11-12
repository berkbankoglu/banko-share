# Banko Share

İki veya daha fazla Windows bilgisayarı tek fare ve klavyeyle kullanın. İmleci ekran kenarından itince diğer bilgisayara geçer; klavye de onunla gider. Geliştirme önizlemesidir.

Yol haritası: [docs/PLAN.md](docs/PLAN.md).

## İndir ve kur

[En güncel sürüm ve kurulum dosyası](https://github.com/berkbankoglu/banko-share/releases/latest)

İki bilgisayarda da **Setup.exe** ile biten dosyayı indirip çalıştırın. .NET ayrıca kurulmaz. Sonraki sürümleri uygulama otomatik alır.

İlk açılışta Windows güvenlik duvarı izin isteyebilir. **Özel ağlar** için izin verin. Ağınız "Ortak" görünüyorsa ya ağı "Özel" yapın ya da ortak ağ kutusunu da işaretleyin; aksi hâlde bilgisayarlar birbirini bulamaz.

## İlk kurulum

1. İki bilgisayarda Banko Share'i açın. Aynı anda ShareMouse gibi başka bir fare paylaşım uygulaması çalışıyorsa kapatın.
2. Eklenecek bilgisayarda **Cihazlar → Eşleştirme kodu göster** seçin. Her basışta yeni bir 6 haneli kod çıkar. Kod 5 dakika geçerlidir ve bir kez kullanılır.
3. Diğer bilgisayarda **Ağda bulunan Banko Share cihazları** listesinden onu seçin (veya IP adresini yazın), 6 rakamı girip **Eşleştir** seçin. İki bilgisayarda da 0.4.0 veya üstü olmalıdır.
4. **Monitör düzeni** sayfasında monitörleri masanızdaki yerleşime göre sürükleyin. Bir bilgisayarın monitörleri birlikte taşınır ve yakındaki kenara yapışır. Düzen iki bilgisayarda aynı kalır.
5. İmleci, düzende diğer bilgisayara değen kenara itin.

Eşleştirme kalıcıdır. Sonraki açılışlarda iki uygulama birbirini bulup kendiliğinden bağlanır.

## Kullanım

- **Geçiş:** İmleç, düzende komşu monitör olan kenardan geçer; aynı yoldan geri döner. Klavye imleçle birlikte gider.
- **Panik tuşu:** Esc'e hızlıca iki kez basmak imleci ve klavyeyi bu bilgisayara geri getirir.
- **Geri alma:** Kontrol edilen bilgisayarın kendi faresini oynatmak veya tıklamak kontrolü ona verir.
- **Güvenli durumlar:** Fare düğmesi basılıyken geçiş yapılmaz. Bağlantı koparsa, ekran kilitlenirse veya bilgisayar uykuya geçerse imleç eve döner ve karşı tarafta basılı tuşlar bırakılır.
- **Arka planda çalışma:** Pencereyi kapatmak uygulamayı kapatmaz; Banko Share bildirim alanında (saatin yanında) paylaşmaya devam eder.
  - Simgeye tıklayınca pencere açılır.
  - Sağ tık menüsünde kenardan geçiş, bu bilgisayara dönme, Windows ile başlatma ve **Çıkış** vardır.
  - Kurulu sürüm Windows açılınca bildirim alanında başlar. Bu, **Uygulama** sayfasından kapatılabilir.
- **Kenardan geçişi kapatma:** **Geçiş ayarları** sayfasından veya bildirim alanı menüsünden yapılır. Kısayollar yine çalışır.
- **Geçiş ayarları** (hepsi varsayılan olarak kapalı):
  - Ekran boyutlarını eşitleme: kenarın her yerinden orantılı geçiş.
  - İmleci ekranların etrafında döndürme.
  - Geçişi engelleme: tam ekran veya büyütülmüş pencere öndeyken, ekran köşelerinde, Shift/Ctrl/sol Alt basılı değilken ya da listedeki programlar öndeyken.
  - Kenara çift dokunarak engeli aşma veya her geçişte çift dokunma isteme.
  - Kısayollar: geçişi aç/kapat, sonraki bilgisayar, bu bilgisayara dön ve Ctrl + sol Alt (ya da Ctrl + Shift) + rakam ile monitöre atlama. AltGr kısayol sayılmaz.

## Nasıl çalışır

- C# / .NET 9 / WPF. Görünüm BankoSpace'in Noir tasarımını izler.
- Fare ve klavye kancaları arayüzden ayrı bir thread'de çalışır. Uygulama per-monitor DPI farkındalığıyla çalışır; bütün koordinatlar fiziksel pikseldir.
- Karşı bilgisayardan gelen giriş (SendInput), imleç taşıma ve gizleme ayrı bir thread'de, hiçbir kilit tutulmadan yapılır. Windows SendInput'u, oturumdaki bütün düşük seviye kancalar olayı görene kadar bekletir. Kilit tutulurken çağrılsaydı aynı anda gelen fiziksel bir fare hareketi iki tarafı birbirine kilitlerdi.
- Windows'un düşük seviye fare kancası kenara itilen imleç için kenarın ötesindeki noktayı bildirir. Geçiş buna ve paylaşılan monitör düzenine göre belirlenir.
- İmleç diğer bilgisayardayken yerel imleç ana ekranın ortasında görünmez biçimde bekletilir. Hareket farkları karşı bilgisayara **mutlak konum** olarak gönderilir; böylece fare ivmesi iki kez uygulanmaz. Ağ yoğunlaştığında araya giren konumlar birleştirilir.
- Her cihazın, Windows anahtar deposunda dışa aktarılamayan kalıcı bir RSA anahtarı ve kendinden imzalı sertifikası vardır. Sertifikanın SHA-256 parmak izi cihaz kimliğidir.
- Bağlantılar karşılıklı TLS 1.2/1.3 ile kurulur ve iki taraf da birbirinin parmak izini sabitler.
- Eşleştirme kodu 6 rakamdır ve ağda hiç gönderilmez. Kod, SPAKE2 parola doğrulamalı anahtar değişiminin parolasıdır. Grup, RFC 3526'daki 2048 bitlik güvenli asal grubudur.
  - Değişim, iki tarafın TLS sertifika parmak izlerine bağlıdır. Araya giren biri doğru kodu iletse bile eşleştirme başarısız olur.
  - Ağdaki bir saldırgan kodu bilgisayarında deneme-yanılmayla çözemez. Her bağlantı denemesi tek bir tahmine izin verir ve 5 denemede kod iptal olur.
- Ağda bulma UDP 24820 yayınıyla yapılır. Bu yalnızca adres ipucudur; güven her zaman sabitlenmiş sertifikadan gelir. Bağlantılar TCP 24819 üzerindendir.
- Her bağlantıda 0,5 sn'de bir kalp atışı gider; 3 sn sessizlik bağlantıyı kapatır. Kopan bağlantılar artan bekleme süreleriyle yeniden kurulur.
- Hesap, bulut sunucusu, telemetri veya tuş kaydı yoktur.
- Windows ile başlatma, kullanıcıya ait `HKCU\…\Run` kaydıyla yapılır ve yalnızca Setup ile kurulan sürüm için açılır.
- Ayarlar `%LocalAppData%\BankoShare.Settings` içinde tutulur: eşleşen cihazlar, düzen, tercihler ve açık sertifika. Dosyalar arka planda, geçici dosya üzerinden atomik olarak yazılır. Virüs tarayıcısı dosyayı kısa süre tutarsa yazma yeniden denenir. Fare kancası diske hiç beklemez.

## Güncellemeler

- Uygulama açılışta ve her 10 dakikada herkese açık GitHub Releases kaynağını kontrol eder; hesap veya anahtar gerekmez.
- Yeni paket arka planda indirilir ve Velopack ile doğrulanır.
- İmleç diğer bilgisayardayken, bu bilgisayar kontrol edilirken veya son bir dakikada paylaşım olduysa kurulum bekletilir. Boşta kalınca 15 saniyelik geri sayımla güncellenip yeniden açılır.
- Otomatik kurulum **Güncellemeler** sayfasından kapatılabilir.
- Windows kod imzalama sertifikası henüz yoktur.

Yeni sürüm yayımlama: [docs/RELEASING.md](docs/RELEASING.md).

## Geliştirme

Windows üzerinde .NET 9 SDK ile:

```powershell
dotnet build BankoShare.sln -c Release
dotnet run --project tests/BankoShare.Tests -c Release
dotnet run --project src/BankoShare.App -c Release
```

Testler gerçek loopback TLS bağlantılarıyla eşleştirme, yanlış kod ve sertifika reddi, yeniden bağlanma, çift bağlantı ayıklama, düzen eşitleme ve iki denetleyici arasında uçtan uca geçişi sınar. Geçiş motoru, gerçek geliştirme düzeni (üç monitörlü bilgisayarın üstünde tek monitörlü bilgisayar) üzerinde test edilir. Testler işletim sistemine giriş göndermez. Gerçek cihaz doğrulamaları [docs/TESTING.md](docs/TESTING.md) içindedir.

## Sınırlar

- Yönetici olarak çalışan pencereler, UAC, kilit ekranı ve Ctrl+Alt+Del normal kullanıcı uygulamasına kapalıdır.
- Pano, dosya aktarımı ve hız ayarları henüz yok (bkz. [yol haritası](docs/PLAN.md)).
- Tam ekran ve program koşulları yalnızca kontrol eden bilgisayardaki ön pencereye bakar; kontrol edilen bilgisayardaki tam ekran uygulama geçişi engellemez.
- Fare takılı olmayan bir bilgisayarda Windows imleci çizmeyebilir; imleç çizme Aşama 3'te.
- Başlangıçta iki bilgisayarda aynı klavye düzenini kullanın.
- Süreç zorla öldürülürse karşı taraf bağlantı kopmasını 3 saniyede fark edip basılı girişleri bırakır; yine de fiziksel klavye/fare erişimini koruyun.

## Teknik referanslar

- [LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)
- [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) ve [MOUSEINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput)
- [Per-monitor DPI awareness](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
- [.NET SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream)

Özgün uygulama; ShareMouse veya başka projelerden kaynak kod alınmamıştır. Açık kaynak lisansı henüz seçilmedi.
