# بوابة حالة LeanOptionsLab المعزولة

هذه البوابة خدمة HTTP دائمة للقراءة فقط فوق تقارير المختبر. لا تشغّل LEAN، ولا تبدأ Backtest، ولا تقبل ملفات، ولا تحتوي مساراً يكتب حالة أو يرسل أمراً. يبقى تشغيل المختبر عملية batch مستقلة، وتقرأ البوابة نسخة تقرير واحدة مركّبة داخل الحاوية للقراءة فقط.

## العقد العام

| المسار | المعنى |
| --- | --- |
| `/` | تعريف الخدمة وروابط المسارات |
| `/healthz` | عملية البوابة تستجيب |
| `/readyz` | التقرير المركّب موجود وصحيح البنية؛ `experimentReady` يبقى مستقلاً |
| `/api/v1/status` | ملخص آمن للحالة دون السجل أو رسائل فشل البيانات |

كل طريقة HTTP غير `GET` تُرفض بـ`405`. الملخص يثبت `liveTrading=false` و`paperTrading=false` ويعرض أعداد الأحداث فقط. غياب التقرير أو فساده يعيد `503` من مساري الجاهزية والحالة.

## حدود الحاوية

- صورة .NET 10 مثبتة بالـdigest، وتشغيل بمستخدم غير `root`.
- منفذ المضيف مربوط بـ`127.0.0.1` فقط؛ Envoy هو نقطة TLS العامة الوحيدة.
- نظام الملفات للقراءة فقط، مع `/tmp` مؤقت محدود.
- إسقاط جميع Linux capabilities وتفعيل `no-new-privileges`.
- حدود للذاكرة والمعالج وعدد العمليات، وسياسة `restart: always`.
- تركيب مجلد تشغيل واحد فقط تحت `/results/<run-id>` للقراءة فقط.
- لا توجد اعتمادات حساب أو بيانات سوق داخل الصورة أو المستودع.

## البناء والتشغيل

من checkout مثبت ونظيف، وبعد إنتاج `comparison-report.json` داخل مجلد تشغيل محدد:

```bash
export GATEWAY_IMAGE_TAG="$(git rev-parse HEAD)"
export GATEWAY_RUN_ID="<RUN_ID>"
export GATEWAY_RESULT_SOURCE="/opt/lean-options-lab/shared/results/$GATEWAY_RUN_ID"
export GATEWAY_HOST_PORT="18080"

test "$(git rev-parse HEAD)" = "$GATEWAY_IMAGE_TAG"
test -f "$GATEWAY_RESULT_SOURCE/comparison-report.json"
docker compose -f compose.gateway.yaml config --quiet
docker compose -f compose.gateway.yaml up --detach --build
```

التحقق المحلي على الخادم قبل ربط Envoy:

```bash
curl --fail --silent --show-error http://127.0.0.1:18080/healthz
curl --fail --silent --show-error http://127.0.0.1:18080/readyz
curl --fail --silent --show-error http://127.0.0.1:18080/api/v1/status
```

`readyz=200` يعني أن البوابة تستطيع قراءة تقرير صحيح، ولا يعني أن تجربة الأوبشن قابلة للترتيب. المرجع هو `experimentReady` و`finalStatus` داخل الاستجابة.

## النشر العام والرجوع

المضيف المختار هو `wasemsaa.cloud`: DNS يشير إلى الخادم، وشهادة Envoy الحالية تغطي الاسم. يربط Envoy هذا المضيف بالمنفذ المحلي فقط؛ لا يُفتح منفذ الحاوية على الشبكة العامة.

قبل تعديل Envoy تُحفظ نسخة مؤرخة من `/etc/envoy/envoy.yaml`، ثم يُتحقق من الإعداد الجديد قبل إعادة التحميل. الرجوع يعيد النسخة السابقة، يتحقق منها، يعيد تحميل Envoy، ثم يوقف مشروع Compose الخاص بالبوابة فقط. لا تُحذف إصدارات التطبيق أو نتائج المختبر أثناء الرجوع.

## معيار القبول

لا يعد النشر مكتملاً إلا بعد تحقق كل الآتي:

1. صورة مبنية من SHA المدموج نفسه.
2. الحاوية `healthy` وتعمل بغير `root` مع ضوابط العزل أعلاه.
3. المنفذ ظاهر على loopback فقط.
4. TLS العام ناجح على `https://wasemsaa.cloud`.
5. `POST` العام مرفوض، ولا توجد نقطة تشغيل أو كتابة.
6. الحالة العامة تطابق التقرير المركّب، بما في ذلك `invalid-data` عند نقص البيانات.
7. خدمات الخادم الأخرى وحاوياتها بقيت سليمة قبل النشر وبعده.
