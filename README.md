# LeanOptionsLab — مختبر محلي لأوبشن الأسهم الأميركية

هذا مختبر بحث وتدقيق محلي مبني على LEAN وC#. يشغّل LEAN Launcher الرسمي مباشرة على Windows. لا يوجد فيه وسيط، أو حساب، أو تداول حي، أو أوامر حقيقية، أو تنزيل بيانات سوق، أو توصية مالية.

## النطاق الثابت في v1

- الأصل: SPY
- الدقة: دقيقة واحدة
- المدة الكلية: 2021-01-01 إلى 2025-12-31
- التدريب: 2021 إلى 2023
- التحقق: 2024
- خارج العينة: 2025
- قوالب المقارنة: Put Credit Vertical وCall Credit Vertical وDirectional Debit Vertical

ملف الإعداد المتتبع هو LeanOptionsLab/configs/experiment.v1.json. قواعد الدخول والخروج وحجم الصفقة، وكذلك العمولة والانزلاق ومصدرهما، متروكة عمداً بلا قيم معتمدة. لهذا لا يستطيع المختبر إعلان فائز أو إنشاء صفقة من تلقاء نفسه.

## الحواجز ضد الترتيب الزائف

يصدر التقرير إحدى الحالات التالية فقط:

- ranked: تتحقق بيانات جميع الاستراتيجيات، وتكون قواعدها وتكاليفها معتمدة، وتوجد مقاييس خارج العينة صالحة بلا تعادل في القمة.
- not-rankable: القواعد أو تكاليف التنفيذ أو اكتمال تقييم الاستراتيجيات لا يحقق شرط الترتيب.
- invalid-data: ينقص Security Master أو Trade/Quote الدقيقة المطلوبة أو يفشل طلب بيانات.

الترتيب يقرأ العائد المعدل بالمخاطر والهبوط الأقصى من قسم خارج العينة فقط. يحمل التدريب والتحقق كدليل مراجعة ولا يدخلان في تعبير الترتيب.

## المتطلبات المحلية

- .NET SDK 10.0.400 أو إصدار 10.x متوافق.
- نسخة LEAN الرسمية في `.tools/lean-engine` عند commit `abeb0a0627ec484b92291c45c3f2553726c26199`.
- مراجع `QuantConnect.Lean` و`QuantConnect.DataSource.Libraries` مثبتة على `2.5.18042`.

لتهيئة المحرك من البداية، استخدم نسخة المصدر المثبتة فقط:

~~~
git clone --filter=blob:none https://github.com/QuantConnect/Lean.git .tools\lean-engine
git -C .tools\lean-engine checkout --detach abeb0a0627ec484b92291c45c3f2553726c26199
dotnet build .tools\lean-engine\Launcher\QuantConnect.Lean.Launcher.csproj --configuration Release --nologo
~~~

`lean.json` يعرّف بيئة backtesting والموفر المحلي فقط. السكربتات تمرر إليه صراحة مسار DLL واسم الخوارزمية ومجلد `data/` ومجلد `results/<run-id>`؛ ولا تقبل بيانات اعتماد أو إعدادات وسيط.

عند أول تشغيل، ينسخ السكربتان ملفي LEAN الثابتين `symbol-properties-database.csv` و`market-hours-database.json` من المصدر المحلي إلى `data/` إن كانا غائبين. هذان متطلبا بدء للمحرك وليسا بيانات أسعار أو عقود أو تنزيل شبكة، ويبقيان تحت `data/` المستبعد من Git.

## الفحص والتشغيل

شغّل اختبارات C# الذاتية:

~~~
dotnet run --project .\LeanOptionsLab.Tests\LeanOptionsLab.Tests.csproj
~~~

تحقق من شكل إعداد التجربة:

~~~
dotnet run --project .\LeanOptionsLab.Tooling\LeanOptionsLab.Tooling.csproj -- validate --config .\LeanOptionsLab\configs\experiment.v1.json
~~~

لتشغيل بوابة الجودة المحلية كاملة قبل أي مراجعة أو التزام مستقبلي:

~~~
.\scripts\Invoke-LocalQualityGate.ps1
~~~

اختبر بناء وتشغيل LEAN Launcher المحلي من دون اشتراك بيانات أو أمر:

~~~
.\scripts\Invoke-LocalLeanSmoke.ps1
~~~

لتشغيل المختبر الحقيقي بعد توفير بيانات محلية مدققة:

~~~
.\scripts\Invoke-LocalLeanBacktest.ps1
~~~

لا تمرر بيانات مخترعة. عند تشغيل المختبر بلا بيانات محلية، ينشئ التقرير حالة `invalid-data` ولا يخرج ترتيباً. إن أعاد المحرك رمز خروج لنقص البيانات، يكتب السكربت التقرير أولاً ثم يعيد رمز الخروج نفسه حتى لا يخفي الفشل.

## متطلبات البيانات قبل الترتيب

لا يتحقق ترتيب v1 إلا بعد تقديم دليل مدقق لكل تشغيل يؤكد:

1. US Equity Security Master.
2. بيانات Trade دقيقة واحدة لـSPY.
3. بيانات Trade دقيقة واحدة لعقود US Equity Options.
4. بيانات Quote دقيقة واحدة لعقود US Equity Options.
5. عدم وجود طلبات بيانات فاشلة.

لا تدخل Greeks أو IV أو Option Universe Data في v1. لا تستخدم أي سعر عمولة أو انزلاق حتى يتوفر مصدر معتمد وتسجل قيمته صراحة في experiment.v1.json.

## المخرجات القابلة للتدقيق

كل تشغيل يملك مجلداً صريحاً في results/<run-id>. أداة التقرير تكتب:

- comparison-report.json
- comparison-report.ar.md

وتحتوي الإعداد، نسخة الكود، قرار تحقق البيانات، قرار الترتيب، وأحداث Order/Assignment/Exercise. سكربت Backtest يلتقط تلقائياً علامات `OPTIONS_LAB` من أول ملف سجل داخل مجلد التشغيل، ويمكن تمرير سجل محدد عبر LeanLogPath عند الحاجة. مجلدات data وresults وlogs وstorage وملفات الاعتماد مستبعدة من Git.
