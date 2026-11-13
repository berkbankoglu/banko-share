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
- **Güvenli durumlar:** Fare düğmesi basılıyken (bu bilgisayarda ya da diğerinde, ör. pencere sürüklerken) geçiş yapılmaz. Bağlantı koparsa, ekran kilitlenirse veya bilgisayar uykuya geçerse imleç eve döner ve karşı tarafta basılı tuşlar bırakılır.
- **Arka planda çalışma:** Pencereyi kapatmak uygulamayı kapatmaz; Banko Share bildirim alanında (saatin yanında) paylaşmaya devam eder.
  - Simgeye tıklayınca pencere açılır.
  - Sağ tık menüsünde kenardan geçiş, bu bilgisayara dönme, Windows ile başlatma ve **Çıkış** vardır.
  - Kurulu sürüm Windows açılınca bildirim alanında başlar. Bu, **Uygulama** sayfasından kapatılabilir.
- **Pano ve dosyalar:** Kopyaladığınız şey imleçle birlikte diğer bilgisayara geçer; orada Ctrl+V ile yapıştırılır. İmleç geri gelince orada kopyalanan da buraya gelir.
  - İmleç diğer bilgisayardayken orada kopyalanan metin ve resim hemen bu bilgisayara da gelir.
  - Kontrol edilen bilgisayarın kendi faresi devreye girerse, oradaki pano da imlecin döndüğü bilgisayara gider.
  - Kopyalar Windows'un pano sıra numarasıyla izlenir; bir değişiklik bildirimi kaçsa bile kopya imleçle birlikte gider.
  - Metin, HTML, RTF ve resim desteklenir.
  - Dosya ve klasörler Gezgin'de kopyalanıp diğer bilgisayarda yapıştırılır; bir seferde en fazla 8 GB ve 50.000 öğe.
  - Gelen dosyalar yapıştırılana kadar `%LocalAppData%\BankoShare.Settings\Alınan dosyalar` içinde bekler; üç günden eskiler silinir.
  - Dosyalar büyük olabileceği için yalnızca imleç diğer bilgisayara geçince gider.
  - Gelen içerik geldiği bilgisayara geri gönderilmez.
- **Başka paylaşım programı:** ShareMouse, Synergy, Barrier, Input Leap, Deskflow veya Mouse Without Borders açıkken Banko Share kendini duraklatır ve nedenini yazar. İki program aynı fareyi yönetirse sürükleme ve tıklamalarda imleç donar. Diğer program kapanınca Banko Share kendiliğinden devam eder.
- **Oyun kilidi** (**Geçiş ayarları** sayfası, varsayılan olarak açık): Oyun öndeyken imleç ve klavye o bilgisayarda kalır. Diğer bilgisayara geçmek için Alt+Tab ile oyundan çıkılır; oyuna dönünce kilit yeniden devreye girer.
  - VALORANT ve League of Legends her zaman kilitlenir. Yalnızca Riot istemcisinin açık olması yetmez.
  - Steam'in "çalışıyor" diye işaretlediği oyun kendiliğinden kilitlenir: Steam'in kayıt defteri değerinden ve oyunun kurulum klasöründeki program adlarından tanınır. Windows'un oyun olarak tanıdığı programlar (Game Bar listesi) da kilitlenir. Başka oyunlar listeye eklenebilir.
  - Oyunun tam ekran ya da pencerede açık olması fark etmez. Listede olmayan bir oyun da, oynarken imleci kendi penceresinde tuttuğu (ClipCursor) sürece kenardan geçirmez. Banko Share bu sınırı yalnızca okur, hiç değiştirmez.
  - İstenirse "oyun açık olduğu sürece kilitle" seçilir; o zaman Alt+Tab da kilidi açmaz.
  - Kilitliyken fare ve klavye kancaları kaldırılır, hiçbir giriş gönderilmez ve diğer bilgisayar oraya geçemez.
  - Kenara çift dokunma ve kısayollar kilidi aşamaz.
  - Diğer bilgisayarın faresi ve klavyesi bir oyun penceresine hiç gönderilmez: oyun öne gelirse ya da arkadaki oyuna tıklanırsa imleç kendi bilgisayarına döner.
  - Oyunun sürecine dokunulmaz; yalnızca süreç listesi, öndeki pencerenin süreç numarası, Steam'in ve Windows'un kayıt defteri değerleri ve oyun klasöründeki dosya adları okunur.
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
- Hesap, bulut sunucusu, telemetri veya tuş kaydı yoktur. Sürücü kurulmaz; her şey kullanıcı düzeyinde çalışır.
- Başka programların süreçleri açılmaz. Program, oyun ve diğer paylaşım programlarının adları Windows'un süreç listesinden okunur. Hile önleme sürücüleri, başka programların oyun süreçlerini açmasını izler.
- Pano içeriği ve dosyalar, giriş bağlantısından ayrı, aynı sabitlenmiş sertifikalarla kurulan ikinci bir TLS bağlantısıyla aktarılır. Alıcı önce başlığı denetler: her yol göreli olmalı, alınan klasörün dışına çıkamaz (`..`, sürücü adı, ayrılmış aygıt adları reddedilir), boyut ve öğe sayısı sınırlıdır ve diskte yer olmalıdır. Yarım kalan aktarımın klasörü silinir.
- Fare kancası bekçisi: imleç hareket ettiği hâlde kancaya 1,5 saniye olay gelmezse kancalar yeniden kurulur. İmleç diğer bilgisayarda değilken görünür kalmış gizleme penceresi kapatılır.
- Tanılama kaydı `%LocalAppData%\BankoShare.Settings\logs\banko-share.log` dosyasına yazılır (2 MB'ta döner); tuşlar ve pano içeriği yazılmaz.
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
- Dosyaları kenarın ötesine sürükle-bırak, kaydırma ve fare hızı ayarları, klavye düzeni eşitleme henüz yok (bkz. [yol haritası](docs/PLAN.md)). Dosyalar kopyala-yapıştır ile aktarılır.
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
