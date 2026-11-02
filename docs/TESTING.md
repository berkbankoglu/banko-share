# İki cihaz kabul testi

Durum: henüz uygulanmadı. Her iki cihazda fiziksel klavye/fare bulunsun. İlk denemeyi boş bir metin düzenleyicide yapın.

| Test | Beklenen |
| --- | --- |
| Alıcı kapalıyken bağlan | Zaman aşımı/hata; yerel kontrol korunur |
| Yanlış eşleştirme kodu | Bağlantı reddedilir; giriş uygulanmaz |
| Doğru kod | Bağlı fakat yerel kontrol açık |
| F12 | Uzak fare ve klavye aktif |
| Fare hareketi, sol/sağ/orta/yan düğmeler, iki tekerlek ekseni | Doğru hareket ve düğme eşleşmesi |
| TR Q harfleri, Shift, AltGr, Ctrl+C/V, Alt+Tab | Hedef klavye düzenine uygun giriş |
| F12 ile geri dönüş | Yerel kontrol hemen geri gelir; uzakta tüm tuş/düğmeler bırakılır |
| Ctrl veya fare düğmesi basılıyken ağı kes | Alıcı yaklaşık 3 saniyede bırakır; gönderici yerel kontrole döner |
| Alıcıda fiziksel F12 | Oturum kapanır; tekrar eşleştirme gerekir |
| Göndericiyi kapat | Alıcı kopmayı algılar ve basılı girişleri bırakır |
| Uygulama odağını kaybet | Gönderici uzak kontrolü kapatır |
| Ekran kilitle / uyku / uyanma | Oturum güvenli biçimde kapanır; yeni bağlantı gerekir |
| %100 / %150 DPI, çift monitör | Hareket doğruluğu ve hız farkları kaydedilir |
| Yönetici penceresi | Desteklenmediği anlaşılır; uygulama çökmez |
| Yüksek polling rate fare / ağ yoğunluğu | Kuyruk taşarsa oturum kapanır; tuş kaybı sessizce geçiştirilmez |

Başarı koşulu: iki cihazda en az 30 dakikalık normal kullanımda takılı tuş, kayıp tıklama ve kontrol kaybı olmaması. Bu sonuç kaydedilmeden günlük kullanıma hazır etiketi verilmez.
