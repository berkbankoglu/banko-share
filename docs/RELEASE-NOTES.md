## Banko Share 0.5.0: pano ve dosya aktarımı, donma önlemleri

Kurulu sürüm bu güncellemeyi kendiliğinden alır. Hemen almak için **Uygulama → Şimdi kontrol et** seçin. Pano paylaşımı için iki bilgisayarda da 0.5.0 olmalı.

**ShareMouse'u kapatın.** Banko Share artık ShareMouse, Synergy, Barrier, Input Leap, Deskflow veya Mouse Without Borders açıkken kendini duraklatır ve nedenini yazar. İki paylaşım programı aynı fareyi yönetirse sürükleme ve tıklamalarda imleç donar.

### Yeni

- **Pano imleci izler:** Bir bilgisayarda kopyaladığınız şey, imleç diğer bilgisayara geçince oraya da geçer; orada Ctrl+V ile yapıştırırsınız. İmleç geri gelince orada kopyaladığınız şey de buraya gelir.
  - Metin, biçimli metin (HTML, RTF) ve resim (ekran görüntüsü dahil) desteklenir.
  - **Dosya ve klasörler:** Gezgin'de kopyalayın, diğer bilgisayarda Ctrl+V ile yapıştırın. Bir seferde en fazla 8 GB ve 50.000 öğe aktarılır.
  - Dosyalar ayrı, şifreli bir bağlantıyla gider; büyük dosyalar fareyi yavaşlatmaz.
  - Büyük aktarımlarda ilerleme durum satırında görünür; bitince bildirim çıkar.
  - **Uygulama → Pano ve dosyalar** bölümünden kapatılabilir.
- **Tanılama kaydı:** Bağlantılar, geçişler, duraklatmalar ve yavaş girişler kayıt dosyasına yazılır; tuşlar ve kopyalanan içerik yazılmaz. **Uygulama → Tanılama** bölümünden diğer bilgisayarın kaydı da alınabilir.
- **Sürüm bilgisi:** Eşleşmiş cihazlar listesinde diğer bilgisayarın sürümü görünür; eski sürümse uyarılır.

### Düzeltmeler

- **Sürüklerken imleç kaçmaz:** Diğer bilgisayarda fare düğmesi basılıyken (ör. pencere sürüklerken) imleç kenardan geri geçmez; düğme bırakılınca geçer.
- **Fare kancası bekçisi:** Windows, yanıt vermeyen bir fare kancasını sessizce kaldırabilir. Banko Share bunu fark edip kancaları yeniden kurar ve bunu kayda yazar. Takılı kalan imleç gizleme penceresi de kendiliğinden kapatılır.
- **Oyun modu daraltıldı:** Yalnızca VALORANT ya da League of Legends oyunu açıkken devreye girer; Riot istemcisinin arka planda açık olması artık Banko Share'i durdurmaz.
- **Hata dayanıklılığı:** Beklenmeyen bir arayüz hatası uygulamayı kapatmaz, kayda yazılır.

Bu bir geliştirme önizlemesidir. Windows kod imzalama sertifikası henüz yoktur.
