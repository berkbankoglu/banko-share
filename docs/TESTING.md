# Doğrulama kayıtları ve kabul testleri

## Metin kopyala-yapıştır — 13.11.2026, v0.5.2

**Bildirim:** Dosya aktarılıyordu, düz metin kopyalanıp yapıştırılamıyordu.

**Kayıtlar (iki bilgisayar da 0.5.1):**

- BANKOARTS'tan DESKTOP'a bir resim (05:58) ve bir PDF (06:05) gitti. DESKTOP ikisini de aldı.
- DESKTOP hiçbir zaman pano göndermedi: on kez "kontrol bitti" var, ama bir gönderim ya da hata satırı yok.
- 0.5.1'de "gönderilmedi" kararları ve bazı aktarım hataları kayda yazılmıyordu. Bu yüzden DESKTOP'taki kesin neden kayıttan çıkarılamadı.
- DESKTOP'ta ShareMouse 05:01'de ve 05:34'te kapatıldıktan 8 saniye sonra yeniden açıldı; o süre boyunca Banko Share duraklatıldı. 05:58'den beri kapalı.

**Gerçek uygulamada yeniden üretme (BANKOARTS):**

- Kurulum:
  - Test kopyasında kenardan geçiş kapalıydı.
  - Diğer bilgisayar rolünü loopback üzerinden eşleşen bir test eşi oynadı.
  - Test eşi imleci hiç oynatmadı: "gir" mesajı imlecin bulunduğu noktayı taşıdı.
  - Kullanıcının pano metni saklanıp geri kondu.
- 0.5.1'de iki eksik görüldü:
  - Kontrol edilen bilgisayarın kendi faresi devreye girince orada kopyalanan metin hiç gönderilmedi.
  - Oturum sürerken kopyalanan metin ancak imleç dönünce gidiyordu.
- 0.5.2, üç çalıştırmada beş kontrolün hepsini geçti:
  - Kopyalanınca anında gönderim (imleç hâlâ oradayken, 34 ms).
  - Kontrol geri alınınca gönderim.
  - Kopyalayıp hemen geçişte gönderim.
  - Gelen metin panoya yazıldı ve geri yollanmadı.

**Otomatik testler:** 68 test. Yeniler:

- Kontrol edilen bilgisayarın kendi faresi devreye girince, panosu kontrolü kaybeden bilgisayara gider.
- Pano defteri:
  - Her kopya her bilgisayara bir kez gider ve kaynağına geri dönmez.
  - Başarısız gönderim bir sonraki geçişte yeniden denenir.
  - Eski bir hata yeni bir gönderimi geri almaz.
  - Geçişten hemen sonraki kopya süresi 3 saniyedir.

## Oyun kilidi — 13.11.2026, v0.5.1

**İstek:** Oyun açıkken geçiş yalnızca Alt+Tab ile oyundan çıkınca yapılabilsin; oyundayken geçiş olmasın; bunun bir kilit ayarı olsun.

**Otomatik testler:** 67 test, yeni kodla üç ardışık çalıştırmada geçti. Yeni testler:

- **Engel:** Oyun öndeyken her itiş engellenir ve "Oyun öndeyken geçiş kilitli" yazılır. Kenara çift dokunma kilidi aşamaz. Oyun arkaya geçince (Alt+Tab) aynı itiş geçer. Kilit kapalıyken engel yoktur.
- **Kilit zamanlaması:**
  - Oyun öne gelince kilit hemen devreye girer.
  - Başka bir pencere 300 ms önde kalınca kilit açılır.
  - Alt+Tab seçicisi açıkken durum değişmez ve bekleme yeniden başlar.
  - Oyun kapanınca kilit hemen açılır.
- **Kısayollar:** Oyun öndeyken "sonraki bilgisayar" ve "monitöre atla" kısayolları reddedilir ve bildirim verir. Alt+Tab'dan sonra çalışır.
- **Kontrol edilen bilgisayar:**
  - Arkadaki oyun penceresinin üzerinde imleç hareketi geçer; tekerlek olayı atılır.
  - Oyun penceresine tıklama atılır; imleç geri döner, basılı Shift bırakılır ve "oyuna tıklayamaz" bildirimi çıkar.
  - Oyun öne gelince sonraki hareket ve tuş basışı gönderilmez, imleç geri döner.
- **Ayarlar:** Eski ayar dosyasında kilit açık ve "oyun öndeyken" modundadır. Ayarlar kaydedilip geri yüklenince aynı kalır. Geçersiz mod ve yinelenen oyun adları temizlenir.

**Gerçek uygulama (BANKOARTS, test kopyası, oyun yerine zararsız bir yedek pencere):**

| An | Kancalar | Öndeki pencere | Durum satırı |
| --- | --- | --- | --- |
| Oyun yok | kurulu | opera | Henüz eşleşmiş cihaz yok |
| Yedek "oyun" önde | kaldırıldı | bankoshare-test-game | Oyun kilidi · test oyunu önde; … Alt+Tab ile oyundan çıkın |
| Oyun simge durumunda | kurulu | opera | Henüz eşleşmiş cihaz yok |
| Oyun kapandı | kurulu | opera | Henüz eşleşmiş cihaz yok |

- İlk denemede kilit, oyun küçüldükten yaklaşık 1 saniye sonra açıldı. Simge durumundaki oyun penceresi artık önde sayılmıyor.
- İkinci denemede kilit, küçültmeden 383 ms sonra açıldı (300 ms bekleme ve 100 ms kontrol aralığı).
- Fare hiç oynatılmadı.

**Henüz denenmedi:** Gerçek VALORANT veya League of Legends ile kilit, ve iki gerçek bilgisayar arasında. Yalnızca oyunun süreç adı ve öndeki pencerenin süreç numarası okunur; Vanguard'ın izlediği oyun sürecine tutamak açılmaz.

## Pano ve dosya aktarımı, donma önlemleri — 13.11.2026, v0.5.0

**Bildirim:**

- DESKTOP'taki Banko Share penceresi uzaktan tutulup sürüklenince donma sürüyordu.
- Kopyalanan şey diğer bilgisayara yapıştırılamıyordu.

**BANKOARTS'ta bulunanlar:**

- Banko Share çalışmıyordu; ShareMouse açıktı (00:32'den beri).
- AutoHotkey ve Razer yazılımları da çalışıyordu; bunlar da fare ve klavye kancası kurabilir.
- Banko Share'e ait çökme kaydı yok. Kayıtlardaki çökmeler yalnızca geliştirme sırasında bilerek başarısız edilen test çalıştırmaları.
- Değerlendirme: Banko Share ile ShareMouse aynı anda açıkken ikisi de aynı fareyi yakalıyor; ShareMouse basılı tutarak sürüklemeyi kendi dosya sürükle-bırak özelliği için izliyor. Donmanın en güçlü açıklaması bu. DESKTOP'un sürümü ve DESKTOP'ta ShareMouse'un açık olup olmadığı buradan görülemedi.

**Önlemler:**

- Başka paylaşım programı açıkken Banko Share kendini duraklatır ve nedenini yazar.
- Diğer bilgisayarda düğme basılıyken kenardan geçiş yapılmaz.
- Fare kancası bekçisi: Windows'un sessizce kaldırdığı kancalar yeniden kurulur; takılı kalan imleç gizleme penceresi kapatılır.
- Tanılama kaydı tutulur. Yavaş SendInput (40 ms'den uzun, yani bir kanca yavaş yanıt veriyor) kayda düşer. Diğer bilgisayarın kaydı buradan istenebilir.
- Diğer bilgisayarın sürümü cihaz listesinde görünür.

**Otomatik testler:** 63 test, son çalıştırmalarda 3/3 ve 8/8 geçti. Yeni testler:

- **Sürükleme:** Diğer bilgisayarda düğme basılıyken imleç kenarda kalır; bırakılınca geçer.
- **Pano aktarımı (loopback TLS, ayrı bağlantı):**
  - Gönderilenler: Türkçe karakterli metin, HTML, RTF, resim; içinde boş alt klasör ve 3 MB'lık dosya bulunan bir klasör; tek bir dosya.
  - Hepsi birebir ulaştı ve ilerleme toplama vardı.
- **Ret:** Alıcı reddedince hiçbir veri gitmez. Eşleşmemiş bir sertifika aktarım bağlantısı açamaz.
- **Kötü yollar** hiçbir şey yazılmadan reddedilir: `../`, `Proje/../../`, sürücü adı, `CON`, `a:b`, kök dışı ad, boş bölüm. Aynı adlı kökler, sınır aşan resim de reddedilir.
- **Toplayıcı:** Farklı klasörlerdeki aynı adlı dosyalar "rapor (2).txt" olarak ayrılır. Öğe sayısı ve boyut sınırları uygulanır; olmayan öğe reddedilir.
- **Pano imleci izler:** Geçişte kontrol eden bilgisayar, dönüşte kontrol edilen bilgisayar aktarımı başlatır.
- **Tanılama:** Diğer bilgisayarın kaydı bağlantı üzerinden alınır. Sürüm karşılaştırması doğru çalışır.
- **Test düzeltmesi:** "Crossing" testi bir kez aralıklı başarısız oldu. Neden test ortamıydı: cihaz kaydedilen listeden, motor eşi tanımadan önce görünebiliyordu. Ortam artık motorun eşi tanımasını bekliyor.

**Gerçek Windows panosu (BANKOARTS):** Uygulamanın pano okuma ve yazma kodu gerçek panoda denendi; panoda kullanıcı içeriği yoktu ve test sonunda temizlendi. 12 kontrolün hepsi geçti:

- Yapıştırmaya hazır dosya ve klasör listesi; "kopyala" işareti var.
- Dosya paylaşımı kapalıyken dosya listesi alınmıyor.
- Türkçe karakterli metin, HTML ve RTF birebir korunuyor.
- Alfası sıfır bir ekran görüntüsü opak (Bgr24) olarak kodlanıyor ve 64×48 boyutunda geri okunuyor.

**Gerçek uygulama:**

- ShareMouse açıkken başlatılan test kopyası kendini duraklattı: fare kancaları kurulmadı, durum satırında "ShareMouse açık · … duraklatıldı" yazdı.
- Riot istemcisi açıktı (oyun yoktu). Bu yüzden oyun modu artık yalnızca oyun süreci (VALORANT, League of Legends) için devreye giriyor.

**Henüz denenmedi:** İki gerçek bilgisayar arasında pano ve dosya aktarımı (DESKTOP'un 0.5.0'a güncellenmesi gerekiyor).

## BANKOARTS çökmesi ve oyun modu — 12.11.2026, v0.4.2

**Olay:**

- Ne yapılıyordu: DESKTOP uykuya alındı. İki bilgisayarda da Banko Share 0.4.1 bildirim alanındaydı.
- Ne oldu: BANKOARTS 09:33'te hoparlörden kesintisiz bir ses verip yeniden başladı. Anakarttaki sarı ışık yandı (bu kartta DRAM uyarısı).
- Kayıt: Mavi ekran `0x1E KMODE_EXCEPTION_NOT_HANDLED (c0000005)`. Windows'a göre sorumlu sürücü `vgk.sys` (Riot Vanguard).

**Geçmiş:**

- Aynı sürücü 18.09.2026 ve 06.10.2026'da da mavi ekrana yol açmış (`0x7E`, ikisinde de aynı adres). Bu iki çökme Banko Share'den öncedir. 15.10.2026'da da nedeni kaydedilmemiş bir ani kapanma var.
- Donanım: i7-13700K (13. nesil; vgk.sys çökmelerinde sık geçen işlemci ailesi), mikrokod `0x12F`, ProArt B760-CREATOR D4 (BIOS 3001), 4×16 GB DDR4-3200 XMP.

**Değerlendirme:**

- Çökme Vanguard'ın çekirdek sürücüsünde oldu. Banko Share sürücü kurmaz ve tek başına çekirdeği çökertemez.
- Vanguard'ın fare kancası ve yapay giriş kullanan programları izlemesi nedeniyle tetikleme olasılığı tamamen dışlanamaz. Döküm dosyası yönetici izni gerektirdiği için incelenmedi.
- Bu sürüm, olası etkileşimi en aza indirmek için oyun modunu getiriyor.

**Oyun modu doğrulaması:**

- **Yeni test** (loopback TLS, iki denetleyici):
  - Oturum sürerken duraklatılan makine imleci geri yollar ve basılı tuşları bırakır.
  - Karşı taraf "oyun modunda" görür ve oraya geçemez.
  - Bayrağı tanımayan eski bir eşin "gir" ve tuş çerçeveleri hiçbir giriş üretmez.
  - Devam edince geçiş yeniden çalışır.
- **Protokol testi:** 0.4.0'ın bayraksız Hello mesajı "duraklatılmamış" olarak okunur.
- **Gerçek uygulama:** Test kopyası, oyun yerine zararsız bir yedek süreçle denendi. Riot oyunu adı taşıyan sahte bir süreç bilerek kullanılmadı.
  - Yedek süreç açılınca giriş kancası iş parçacığı ve imleç penceresi kalktı; durum "Oyun modu" oldu.
  - Süreç kapanınca kancalar geri geldi.
  - Vanguard'ın `vgc` hizmeti kapalıyken mod yanlışlıkla açılmadı.
- 56 test üç ardışık çalıştırmada geçti.

## Kontrol edilen bilgisayarda donma — 12.11.2026, v0.4.1

**Bildirim (iki bilgisayarda, 0.4.0):** DESKTOP'a geçip oradaki Banko Share penceresini sürüklemek veya küçültmek imleci donduruyordu. Kurtulmak için DESKTOP'un kendi faresiyle tıklamak gerekiyordu.

**Yeniden üretme (BANKOARTS, gerçek Windows girişiyle):**

- Kurulu uygulamanın yanında ayrı portta (`BANKOSHARE_TEST_PORT`) bir test kopyası açıldı. Bir test eşi bu kopyayı gerçek protokolle kontrol edip penceresini başlık çubuğundan sürükledi ve küçültme düğmesine tıkladı.
- 0.4.0 kodunda, fiziksel fare kıpırdadığı sırada bir hareket **1085 ms** gecikti (normalde 1–15 ms) ve sürükleme bozuldu.
- **Neden:** SendInput, oturumdaki bütün düşük seviye kancalar olayı görene kadar dönmez. Uygulama SendInput'u bir kilit tutarken çağırıyordu. Fiziksel fare olayı gelen kanca da aynı kilidi bekliyordu. İki taraf birbirini Windows'un kanca zaman aşımına (yaklaşık 1 sn) kadar bekledi. Tekrarlanan zaman aşımları Windows'un kancayı kaldırmasına yol açabilir.

**Düzeltme:** Gelen giriş, imleç taşıma ve gizleme, sırası korunarak ayrı bir iş parçacığında ve hiçbir kilit tutulmadan uygulanıyor.

**Doğrulama:**

- Yeni test "fiziksel fare olayı, süren bir enjeksiyonu hiç beklemez":
  - Sahte SendInput, Windows gibi kanca bekleyerek bloke olur; o sırada kontrol edilen tarafa 1 piksellik fiziksel hareket gelir.
  - Eski kodla test başarısız oluyor; düzeltmeyle geçiyor.
- Düzeltilmiş sürümle gerçek deneme:
  - 21 sürükleme adımının hepsi yerine ulaştı, hiç takılma olmadı. Pencere tam 300 piksel kaydı.
  - Sonrasında imleç serbestçe hareket etti.
  - Küçültme tıklaması bu denemede yanlış yere gitti; test betiği düğmenin yerini sürüklemeden önce hesaplamıştı. Düzeltilmiş betikle yapılan son deneme ise kullanıcı fareyi kullandığı için başlamadan durdu (geri alma doğru çalıştı).
- 55 test üç ardışık çalıştırmada geçti.

**Bildirim alanı ve görünüm (test kopyasıyla):**

- Pencereyi kapatınca süreç çalışmaya devam ediyor ve pencere gizleniyor.
- İkinci açılış hemen çıkıyor ve mevcut pencereyi geri getiriyor.
- Dört sayfa Noir temasıyla ekran görüntüsünden kontrol edildi.

## 6 haneli eşleştirme (SPAKE2) — 12.11.2026, v0.4.0

**Neden:** 20 karakterlik kodun ilk 13 karakteri cihazın parmak izi olduğu için kod her seferinde aynı görünüyordu ve yazması zahmetliydi.

**Otomatik testler:** Toplam 54 test geçti; üç ardışık çalıştırmada da sonuç aynı. Eşleştirme testleri:

- **Grup:** RFC 3526 2048 bitlik grubun p ve q = (p−1)/2 değerleri Miller–Rabin testinde asal çıktı. g, M ve N alt gruptadır.
- **Geçersiz değerler:** 0, 1, p−1, p, alt grup dışı p−4 ve yanlış uzunluk reddedilir.
- **Anlaşma:** Aynı kodla iki taraf anlaşır. Farklı kodla da, araya giren bir aracının gördüğü farklı sertifikalarla da anlaşamaz. Sahte onay kabul edilmez.
- **Kod biçimi:** 6 rakam. Boşluk ve tire kabul edilir; başka karakterler reddedilir. Arka arkaya üretilen 50 kod büyük ölçüde farklıdır.
- **Eski protokol:** 0.3 eşleştirme isteği reddedilir.
- **Gerçek TLS ile (loopback):**
  - Doğru kodla eşleşme olur.
  - 5 yanlış deneme kodu iptal eder.
  - Kendi sertifikasıyla araya giren ve doğru kodu ileten bir aracı eşleşemez; kimse aracının sertifikasını kaydetmez.
  - Aynı kod, aracısız bağlantıda hâlâ çalışır.

## Aşama 2 (geçiş ayarları) — 12.11.2026, v0.4.0

**Otomatik testler:** 52 test geçti; altı ardışık çalıştırmada da sonuç aynı. Uygulama sıfır uyarıyla derlendi. Yeni testler:

- **Kenar eşitleme:** DESKTOP'un dışında kalan x=100'den geçiş, DESKTOP'ta 75'e düşer; dönüş yine x=100'e iner.
- **Döndürme:** Soldaki monitörün sol kenarından çıkan imleç sağdaki monitörün sağ kenarına gelir. Orta monitörün alt kenarından çıkan imleç DESKTOP'a üstten girer.
- **Köşe engeli:** Köşede geçiş olmaz. İtme sürerken köşeden çıkılınca geçiş olur. Aynı itişte uyarı bir kez gösterilir.
- **Geçiş tuşu:** Tuş itme sırasında basılınca geçiş olur. Windows'un tuş durumuna bakılır. AltGr'nin ürettiği sahte Ctrl sayılmaz.
- **Tam ekran engeli ve çift dokunma:**
  - Ön pencere denetimi her itişte yalnızca bir kez yapılır.
  - 300 ms arayla iki dokunuş geçer; 600 ms arayla geçmez.
  - Kenar boyunca kaymak dokunuş sayılmaz.
  - Çift dokunma kapalıyken engel aşılmaz.
- **Her geçişte çift dokunma:** Kural iki yönde de geçerlidir. Karşı bilgisayardan dönüşte ön pencere denetimi yapılmaz.
- **Kısayollar:**
  - Bir kez tetiklenir; tuş tekrarı ve tuşun bırakılması yutulur.
  - Yerel sol Alt ile tetiklenince menü maskesi gönderilir.
  - AltGr+2 yazı olarak geçer.
  - Kayıt sırasında kısayollar çalışmaz.
  - Uzaktayken değiştirici tuşlar karşıya gider, kısayolun kendi tuşu gitmez.
- **Atlama ve sıra:**
  - Atlama ve "sonraki bilgisayar" döngüsü çalışır.
  - Monitör harfleri sırası: A = DESKTOP, B = sol, C = orta, D = sağ.
- **Ayar dosyası:**
  - 0.3 dosyası okunur ve gidiş-dönüşte bozulmaz; geçersiz değerler ayıklanır.
  - Kilitli dosyaya yazma, kilit kalkınca tamamlanır.
  - Arka plan yazıcısı yalnızca son değeri yazar.
- **İki denetleyici arasında, loopback TLS üzerinde:**
  - Ctrl+Alt+1 imleci DESKTOP'un ortasına, Ctrl+Alt+3 C'nin ortasına götürür.
  - DESKTOP, basılı Alt'ı bırakmadan önce menü maskesi gönderir.
  - Rakam tuşu DESKTOP'a sızmaz.
  - Engel uyarısı ve aç/kapat uyarısı doğru metinle gelir; ayar diske yazılır.

**Bulunan ve düzeltilen hata:** Ayar dosyasını o anda başka bir işlem açık tutarsa (test okuyucusu veya virüs tarayıcısı), dosyanın yerine yenisini koyma işlemi erişim reddiyle düşüyordu ve ayar kaydedilmiyordu. Test 5 çalıştırmanın 2'sinde bu yüzden başarısız oldu. Düzeltme:

- Okuyucular dosyayı silme paylaşımıyla açıyor.
- Yazma, erişim reddinde de yaklaşık 300 ms boyunca yeniden deneniyor.
- Bütün ayar yazımları (tercihler, düzen, eşleşmeler) arka plandaki tek bir yazıcıya taşındı. Fare kancasının beklediği kilit altında diske hiç yazılmıyor.

**Gerçek uygulama:**

- Geçiş ayarları sayfası ve yeni monitör harfleri ekranda doğru çiziliyor (ekran görüntüsüyle kontrol edildi).
- Kısayol kaydı gerçek klavyeyle denenmedi; o sırada bilgisayar başında çalışılıyordu ve tuş göndermek o işi bozabilirdi.

### İki cihaz kabul testi — Aşama 2

Her satır için yalnızca ilgili ayarı açın, sonra kapatın.

| Test | Beklenen |
| --- | --- |
| Eşitleme açık; C'nin üst kenarının en solundan ve en sağından it | Her ikisinde de A'ya geçer; A'nın alt kenarından dönünce aynı yerden çıkar |
| Döndürme açık; B'nin sol kenarından it | İmleç D'nin sağ kenarına gelir |
| Döndürme açık; C'nin alt kenarından it | İmleç A'ya üstten girer |
| Tam ekran engeli; C'de tam ekran video veya oyun açıkken yukarı it | Geçmez, durum çubuğu nedeni yazar; video B'de açıkken C'den geçiş çalışır |
| Büyütülmüş pencere engeli; C'de büyütülmüş pencere öndeyken it | Geçmez |
| Köşe engeli; C'nin sol üst köşesine yakın it (A eşitlemeyle oradaysa) | Geçmez; ortaya doğru kayınca geçer |
| Geçiş tuşu Shift; Shift'siz ve Shift'le it | Shift'siz geçmez, Shift'le geçer; A'da Shift takılı kalmaz |
| Program listesi (ör. notepad); Not Defteri öndeyken it | Geçmez |
| Engel varken çift dokunma | İki hızlı dokunuşla geçer; tek dokunuşla geçmez |
| Her geçişte çift dokunma | Gidişte ve dönüşte iki dokunuş gerekir |
| Kısayol kaydı: Ctrl+Shift+K'yi aç/kapat'a kaydet, sonra bas | Kutu "Ctrl+Shift+K" gösterir; basınca durum çubuğunda açıldı/kapatıldı yazar |
| Ctrl + sol Alt + 1 / 3 | İmleç A'nın / C'nin ortasına gider; A'da veya BANKOARTS'ta menü çubuğu açılmaz |
| AltGr+Q, AltGr+3, AltGr+7 (TR Q) atlama açıkken | @, #, { yazılır; atlama olmaz |
| Sonraki bilgisayar kısayolu iki kez | A'ya gider, sonra çıktığı yere döner |

## Aşama 1 (kenardan geçiş) — 12.11.2026, BANKOARTS üzerinde

**Otomatik testler:** 39 test geçti; üç ardışık çalıştırmada da sonuç aynı. Uygulama sıfır uyarıyla derlendi. Kapsam:

- Protokol ve eşleştirme kodu.
- Düzen yerleşimi, çakışma çözümü ve kenara yapıştırma.
- Gerçek geliştirme düzeninde geçiş motoru: DESKTOP, 2560×1440 orta monitörün üstünde.
- Loopback TLS ile eşleştirme, yanlış kod ve sertifika, yeniden bağlanma, sessiz eşin kesilmesi ve çift bağlantı ayıklama.
- İki denetleyici arasında uçtan uca geçiş, yazma, geri alma ve eş kaybı.

**Kanca koordinatı ölçümü:** Sol kenar −1920'deyken imleç sola itildi. Düşük seviye kanca −1933 ve −1940 bildirdi; gerçek imleç −1920'de kaldı. Sağ kenarda da aynı sonuç alındı (4479 → 4497). Yani kanca noktası kırpılmaz ve geçiş tespiti bu davranışa dayanır.

**Gerçek uygulama, kontrol edilen taraf:**

- Bir test eşi, çalışan uygulamayla loopback üzerinden gerçek kodla eşleşti.
- Uygulama monitörleri doğru bildirdi: (−1920,0) 1920×1080, (0,0) 2560×1440 ana ekran, (2560,0) 1920×1080; hepsi 96 DPI.
- Test eşi üç monitörün iç noktalarına ve köşe piksellerine mutlak konum gönderdi. **Dokuz noktanın dokuzu tam pikselinde doğrulandı;** gönderimden imlecin varışına kadar 1–6 ms geçti.
- Test sırasında bilgisayar başındaki kullanıcı fareyi oynattı. Uygulama kontrolü geri aldı ve test eşine geri alma mesajı gönderdi. Uygulamanın kendi gönderdiği girişler geri almayı tetiklemedi.
- Test kayıtları sonradan ayarlardan silindi.

**Henüz doğrulanmayanlar (ikinci bilgisayar gerekiyor):**

- Kontrol eden taraf: kenara itince geçiş, yerel imlecin gizlenmesi, geri dönüş.
- İki gerçek bilgisayar arasında ağda bulma, eşleştirme ve düzen eşitleme.

## İki cihaz kabul testi — Aşama 1

Kurulum: BANKOARTS (3 monitör) ve DESKTOP-7EAHGJM (A, orta monitörün üstünde). İki bilgisayarda ShareMouse kapalı, Banko Share Setup ile kurulu. İlk denemeyi boş bir metin düzenleyicide yapın.

| Test | Beklenen |
| --- | --- |
| Ağda bulma | Her iki tarafın "Ağda bulunan" listesinde diğeri görünür |
| Yanlış kod / başka cihazın kodu | Reddedilir; 5 yanlış denemeden sonra kod iptal olur |
| Doğru kod | İki tarafta da "Bağlı"; monitörler düzende görünür |
| A'yı C'nin üstüne sürükle | Kenara yapışır; diğer bilgisayarın düzeni de aynı olur |
| C'nin üst kenarından A'nın altında kalan bölüme it | İmleç A'ya, x konumu korunarak geçer; BANKOARTS imleci görünmez |
| C'nin üst kenarından A'nın dışında kalan bölüme it | Geçiş olmaz |
| A'da gezin, alt kenardan dön | İmleç C'de aynı x'ten çıkar |
| A'da sol, sağ, orta ve yan düğmeler; iki tekerlek ekseni | Doğru eşleşme |
| A'da TR Q harfleri, Shift, AltGr, Ctrl+C/V, Alt+Tab, Win | Hedef düzene uygun giriş; BANKOARTS'ta tetiklenmez |
| Ctrl basılıyken A'ya geç, Ctrl'ü bırak | BANKOARTS'ta Ctrl takılı kalmaz |
| Pencere sürüklerken kenara git | Geçiş olmaz |
| A'dayken Esc, Esc | İmleç C'de çıktığı noktaya döner |
| A'dayken DESKTOP'un kendi faresini oynat | Kontrol DESKTOP'a geçer, BANKOARTS imleci geri gelir |
| A'dayken ağ kablosunu çek | ~3 sn içinde BANKOARTS imleci geri gelir; A'da basılı tuşlar bırakılır |
| Uygulamayı bir tarafta kapat / aç | Diğer taraf kopmayı görür; açılınca kendiliğinden yeniden bağlanır |
| Ekran kilitle / uyku / uyanma | İmleç eve döner; sonra geçiş yeniden çalışır |
| DESKTOP'tan BANKOARTS'a ters yönde geçiş | Aynı davranış |
| Hızlı hareket, yüksek polling rate fare | Takılma veya sıçrama olmaz |
| Yönetici penceresi | Giriş uygulanamaz uyarısı; uygulama çökmez |

Başarı koşulu: İki cihazda en az 30 dakikalık normal kullanımda takılı tuş, kayıp tık, sıçrama veya kontrol kaybı olmaması. Bu sonuç kaydedilmeden günlük kullanıma hazır etiketi verilmez.

## Önceki kayıtlar

### Güncelleme doğrulaması — 02.11.2026

- `v0.2.0` GitHub Actions derleme ve yayın akışları başarılıydı. `--release-smoke 0.2.0` ile herkese açık sürüm keşfi, hesapsız indirme, hash doğrulama, sürüm düşürme engeli ve bozuk paket reddi doğrulandı.
- `0.1.99` olarak paketlenmiş bir kopya, GitHub'dan `0.2.0`'ı alıp uyguladı ve **Güncel · v0.2.0** gösterdi.
