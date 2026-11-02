## Banko Share 0.2.0 — otomatik güncellemeli önizleme

İki bilgisayara da **BankoShare-win-Setup.exe** kurun (varlık adı paketleyici sürümüne göre `BankoShare-Setup.exe` olabilir). .NET'i ayrıca yüklemek gerekmez.

- Uygulama sürümü, son kontrol zamanı ve indirme/kurulum durumu görünür.
- Açılışta ve her 10 dakikada GitHub Releases kontrol edilir.
- Yeni sürüm arka planda indirilip doğrulanır. Aktif bağlantı/alıcı oturumu varsa kurulum bekletilir.
- Oturum yoksa 15 saniye sonra uygulama güncellenip yeniden açılır. Otomatik güncelleme kapatılabilir; elle kontrol ve kurulum da vardır.
- GitHub hesabı veya erişim anahtarı gerekmez.

Eski ham ZIP sürümünün bir kez bu kurulum paketiyle değiştirilmesi gerekir. Sonraki yayımlanan sürümler otomatik gelir. Uygulama kapalıyken kontrol yapılmaz; sonraki açılışta kontrol edilir.

Bu bir geliştirme önizlemesidir. Fare/klavye aktarımının iki fiziksel bilgisayarda kabul testi henüz tamamlanmadı. Windows kod imzalama sertifikası henüz yoktur.
