## Banko Share 0.5.1: oyun kilidi

Kurulu sürüm bu güncellemeyi kendiliğinden alır. Hemen almak için **Uygulama → Şimdi kontrol et** seçin.

### Yeni

- **Oyun kilidi** (**Geçiş ayarları** sayfası, varsayılan olarak açık):
  - Oyun öndeyken imleç ve klavye bu bilgisayarda kalır. İmleç kenardan kaçmaz, diğer bilgisayar da buraya geçemez.
  - Diğer bilgisayara geçmek için **Alt+Tab** ile oyundan çıkın; kilit yaklaşık 0,4 saniyede açılır. Oyuna dönünce kilit hemen yeniden devreye girer.
  - Kenara çift dokunma ve kısayollar (sonraki bilgisayar, monitöre atlama) kilidi aşamaz.
  - İstenirse **Oyun açık olduğu sürece kilitle** seçilebilir; bu, 0.5.0'daki davranıştır.
  - VALORANT ve League of Legends her zaman kilitlenir. Başka oyunlar **Kilitlenen oyunlar** listesine eklenebilir, ör. `cs2.exe`.
- **Oyuna giriş gönderilmez:** Diğer bilgisayarın faresi ve klavyesi bir oyun penceresine hiç gitmez. Oyun öne gelirse ya da arkadaki oyun penceresine tıklanırsa imleç kendi bilgisayarına döner ve nedeni yazılır. Oyunun sürecine dokunulmaz.

### Değişenler

- Oyun kilidi ayarı **Uygulama** sayfasından **Geçiş ayarları** sayfasına taşındı.
- Diğer bilgisayar kilitlendiğinde imleç buraya döner ve "… kilitlendi" bildirimi çıkar.

Bu bir geliştirme önizlemesidir. Windows kod imzalama sertifikası henüz yoktur.
