# الحصول على بيانات السوق وصيغتها على القرص

> **ما الذي يمنع تجربة v1؟** البيانات وقواعد الاستراتيجية غير المعتمدة. المحرّك يعمل والخوارزمية تُهيَّأ، لكن مجلد `data/` لا يحوي SPY، ولا توجد قواعد دخول أو خروج أو تكاليف تنفيذ معتمدة.
>
> كل صيغة موصوفة هنا **مقروءة من المحرّك المثبّت** (`.tools/lean-engine`) ومن أرشيفات العيّنة، لا من مواصفة متخيّلة.

## 1. الفجوة — مقيسة لا مقدّرة

```powershell
.\scripts\Invoke-LocalDataGapReport.ps1
```

يقرأ ملف `failed-data-requests-*.txt` الذي يكتبه LEAN في آخر تشغيل، ويصنّف النقص فعلياً. المقاس في تشغيل `quality-20260901T213828Z-nodata`:

| العدد | المطلوب                                            |
| ----- | -------------------------------------------------- |
| 1,256 | `\option\usa\universes\spy\<DATE>.csv`             |
| 1,255 | `\equity\usa\minute\spy\<DATE>_quote.zip`          |
| 1,255 | `\equity\usa\minute\spy\<DATE>_trade.zip`          |
| 1     | `\equity\usa\daily\spy.zip`                        |
| 1     | `\equity\usa\hour\spy.zip`                         |
| 1     | `\alternative\interest-rate\usa\interest-rate.csv` |

**1,256 يوم تداول** من `2020-12-31` إلى `2025-12-31`، ومنها يوم إحماء قبل بداية التجربة.

⚠️ **هذه القائمة أرضية لا سقف.** المحرّك يطلب البيانات تدريجياً ويتوقف عند أول نقص. الدليل: `interest-rate.csv` لم يظهر حتى بُذرت `map_files` فتقدّم المحرّك أبعد. أعد تشغيل التقرير بعد كل دفعة بيانات.

ينقص من القائمة ملفات الأوبشن بالدقيقة نفسها: المحرّك لا يطلبها إلا بعد أن يقرأ ملف universe لليوم، وهي غائبة كلها، فيتوقف عند الخطوة الأولى. توقّع ظهور `\option\usa\minute\spy\<DATE>_{trade,quote}_american.zip` لكل يوم بعد سد المرحلة الحالية.

## 2. مسار QuantConnect الذي ينفّذه المالك

تسجيل الدخول، قبول اتفاقية البيانات، تأكيد أي تكلفة، والتنزيل أفعال تفاعلية لا ينفّذها أي سكربت في هذا المستودع.

واجهة QuantConnect لسطر الأوامر موجودة محلياً داخل `.tools/lean-cli`، والإصدار المتحقق منه في 2026-09-02 هو `1.0.229`. ابدأ PowerShell من جذر هذا المستودع، واستبدل القيمتين بين `<...>` قبل التنفيذ:

```powershell
$RepositoryRoot = (Resolve-Path -LiteralPath '.').Path
$LeanCli = Join-Path $RepositoryRoot '.tools\lean-cli\Scripts\lean.exe'
$CliRoot = '<ABSOLUTE_EMPTY_DIRECTORY_OUTSIDE_THIS_REPOSITORY>'

if (-not (Test-Path -LiteralPath $LeanCli -PathType Leaf)) {
    throw "QuantConnect CLI is missing: $LeanCli"
}

if (-not [IO.Path]::IsPathFullyQualified($CliRoot)) {
    throw 'CliRoot must be an absolute path.'
}

$repositoryPrefix = $RepositoryRoot.TrimEnd('\') + '\'
$cliRootFullPath = [IO.Path]::GetFullPath($CliRoot)
if (($cliRootFullPath.TrimEnd('\') + '\').StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'CliRoot must be outside this repository.'
}

if (Test-Path -LiteralPath $cliRootFullPath) {
    throw 'Choose a new path that does not already exist.'
}

New-Item -ItemType Directory -Path $cliRootFullPath | Out-Null
Set-Location -LiteralPath $cliRootFullPath

& $LeanCli login
if ($LASTEXITCODE -ne 0) { throw "lean login failed with exit code $LASTEXITCODE" }

& $LeanCli init --organization '<ORGANIZATION_NAME_OR_ID>' --language csharp
if ($LASTEXITCODE -ne 0) { throw "lean init failed with exit code $LASTEXITCODE" }

& $LeanCli data download
if ($LASTEXITCODE -ne 0) { throw "lean data download failed with exit code $LASTEXITCODE" }
```

- شغّل `login` تفاعلياً: لا تمرّر معرّف المستخدم أو API token في سطر الأوامر، ولا تستخدم `--show-secrets`. الاعتماد عام لحساب Windows ويُحفظ في `~/.lean/credentials` خارج المستودع.
- `init` لا يصادق الجذر؛ بل يربطه بالمنظمة المختارة وينشئ `lean.json` و`data/` داخله. لذلك يجب أن يكون المسار جديداً وخارج المستودع.
- استخدم معالج `data download` التفاعلي. لا تستخدم `--yes`: راجع المنتجات والاستحقاقات واتفاقية الاستخدام والسعر الذي يظهر من حسابك، وأكّد أي دفع بنفسك.
- بعد اكتمال التنزيل، انسخ **فقط** المسارات التي يعرضها تقرير الفجوة إلى `data/` في هذا المستودع مع الحفاظ على بنيتها النسبية. لا تستبدل ملفات أخرى بالجملة.
- أعد `Invoke-LocalLeanBacktest.ps1` ثم `Invoke-LocalDataGapReport.ps1`. ظهور متطلبات جديدة متوقع لأن القائمة الحالية أرضية وليست سقفاً.

تشغيل الأداة من هذا المستودع نفسه غير صحيح لأن `lean.json` الحالي يخص Launcher المحلي، لا جذر CLI المنظمي.

### 2.1 ترتيب حجم التكلفة

الأرقام التالية مقروءة من صفحات مجموعات بيانات QuantConnect في **2026-09-02**. الاستحقاق الفعلي والسعر النهائي يظهران من حسابك عند التنزيل، وقد يتغيّران؛ عامِل هذا القسم كترتيب حجم لا كفاتورة.

| البند                      | السعر المنشور                | ما يغطّيه الملف                   |
| -------------------------- | ---------------------------- | --------------------------------- |
| صرف الرصيد                 | `1 QCC = $0.01`              | —                                 |
| أوبشن الأسهم — دقيقة       | **`15 QCC` = `$0.15`** للملف | رمز واحد ليوم واحد، **بكل عقوده** |
| أسهم أميركية — دقيقة       | **`5 QCC` = `$0.05`** للملف  | رمز واحد ليوم واحد                |
| اشتراك جماعي — أوبشن دقيقة | `$1,200` سنوياً              | الكون كامل                        |
| اشتراك جماعي — أسهم دقيقة  | `$600` سنوياً                | الكون كامل                        |

### ⚠️ مضاعف غير مؤكَّد يسبق أي حساب

الصفحة تقول إن الملف يغطّي «رمزاً ليوم واحد». **لكن القرص يحمل ملفات منفصلة لكل نوع tick في اليوم الواحد** — وهذا مرئي في العيّنة وفي تقرير الفجوة معاً:

```text
20151224_trade_american.zip        ← أوبشن، صفقات
20151224_quote_american.zip        ← أوبشن، عروض
20151223_openinterest_american.zip ← أوبشن، فائدة مفتوحة
20151224_trade.zip / _quote.zip    ← سهم، ملفان منفصلان
```

فهل «الملف» في التسعير وحدة بيع واحدة لليوم تشمل الأنواع، أم ملف لكل نوع؟ **لم أتحقق.** لذلك كل رقم أدناه نطاق لا قيمة واحدة.

تقدير تجربة v1 على **1,255 يوم تداول** لرمز واحد:

| البند           | المضاعف `×1` | المضاعف `×2` |
| --------------- | ------------ | ------------ |
| أوبشن SPY دقيقة | `$188`       | `$377`       |
| سهم SPY دقيقة   | `$63`        | `$126`       |
| **المجموع**     | **`≈ $251`** | **`≈ $503`** |

المضاعف `×2` هو الأرجح للأسهم على الأقل، لأن `_trade.zip` و`_quote.zip` ملفان مستقلان فعلاً على القرص ويطلبهما المحرّك بشكل منفصل. رقم `$63` يُرجَّح أنه نصف الحقيقة.

⚠️ **وبند ثالث لم أتحقق منه إطلاقاً: ملفات `option/usa/universes/`.** نتيجة بحث واحدة ذكرت `100 QCC = $1` للملف، ولم أجد صفحة رسمية تؤكّد السعر **ولا نطاق الملف**. الفرق حاسم: إن غطّى الملف رمزاً لكل تاريخه فالتكلفة `$1`، وإن غطّى رمزاً ليوم واحد فهي `1,256 × $1 ≈ $1,256` — أي أضعاف بقية التجربة.

**النتيجة الصادقة: التجربة تقع بين `$251` و`$1,760` تقريباً، ولا يمكن تضييق النطاق إلا من معالج التنزيل في حسابك.** راجع السعر المعروض قبل التأكيد، ولا تعتمد على أي رقم هنا كفاتورة.

الخلاصة العملية: حتى عند الحد الأعلى، شراء رمز واحد بالملف يبقى منافساً للاشتراك الجماعي (`$1,800` سنوياً للمنتجين معاً) — والاشتراك يعطي الكون كامل لا رمزاً واحداً.

> بوابة الجودة تسمح باسم CLI التنفيذي داخل هذه الوثيقة وحدها لغرض الحصول على البيانات. مراجع التشغيل القديمة تبقى ممنوعة في كل المستودع، واسم CLI يبقى ممنوعاً في الكود والسكربتات والإعدادات وREADME حتى لا يعود مسار تشغيل بديل.

## 3. الصيغة على القرص — لأي مصدر آخر

إن اخترت تغذية غير QuantConnect، فهذه هي الصيغة التي يقرأها المحرّك المثبّت.

### 3.1 مسار الأرشيف

```text
data/option/usa/minute/<underlying>/<YYYYMMDD>_<type>_<style>.zip
```

- `<underlying>` رمز **الأصل** بحروف صغيرة، لا رمز العقد. المصدر: `LeanData.cs` — خيارات الأسهم تستخدم رمز الأصل في المسار.
- `<type>`: `trade` أو `quote` أو `openinterest`.
- `<style>`: `american` لأوبشن الأسهم الأميركية.

### 3.2 اسم الإدخال داخل الأرشيف

```text
<YYYYMMDD>_<underlying>_minute_<type>_<style>_<right>_<strike×10000>_<expiry YYYYMMDD>.csv
```

مثال حقيقي من العيّنة:

```text
20151224_goog_minute_quote_american_call_10000000_20151224.csv
```

`10000000` هو سعر التنفيذ **مضروباً في 10,000**، أي `1000.00`. المصدر: `.tools/lean-engine/Common/Util/LeanData.cs:915` لملفات Hour/Daily (`parts[4]`) و`LeanData.cs:923` لملفات الدقة الأدنى ومنها Minute (`parts[6]`)؛ كلاهما يقسم على `10000m`.

### 3.3 صفوف CSV

الدالة المولّدة هي `LeanData.GenerateLine`. لدقة الدقيقة:

**Trade** — 6 حقول:

```text
ms_from_midnight, open, high, low, close, volume
```

**Quote** — 11 حقلاً:

```text
ms_from_midnight, bid_open, bid_high, bid_low, bid_close, bid_size, ask_open, ask_high, ask_low, ask_close, ask_size
```

**OpenInterest** — حقلان: `ms_from_midnight, value`.

### 3.4 فخّان في الأرقام

**أ) الأسعار مضروبة في 10,000 — لكن ليس في كل مكان.**

داخل ملفات العقود، `Scale(value) = value * 10_000m` (`LeanData.cs:951-954`). فالصف:

```text
39420000,3555400,3555400,3555400,3555400,1
```

يعني سعراً قدره **355.54** لا 3,555,400.

أما ملف universe فيكتب أرقاماً عشرية عادية. هذا الجزء مقروء من أرشيف العيّنة، لا من الكاتب؛ ملف universe لا يولّده `GenerateLine` ولم يُحدّد موضع كاتبه:

```text
20151231,450,C,300.0500,301.1500,296.7500,298.3000,0,,1.7405772,...
```

`300.0500` هي `300.05` كما هي. أي أن الملفين يستخدمان تمثيلين مختلفين في التشغيل نفسه.

**ب) الطابع الزمني بالمللي ثانية من منتصف الليل بتوقيت البورصة.**

`34200000` = 09:30، و`39420000` = 10:57. ليس Unix time.

### 3.5 ملف universe

```text
data/option/usa/universes/<underlying>/<YYYYMMDD>.csv
```

كل ما في هذا القسم مقروء من ملف عيّنة (`20151224.csv`)، لا من كاتب في المحرّك. تحقّق منه مقابل مصدرك قبل الاعتماد. ترويسته من العيّنة:

```text
#expiry,strike,right,open,high,low,close,volume,open_interest,implied_volatility,delta,gamma,vega,theta,rho
```

السطر الأول بعد الترويسة يحمل حقول expiry/strike/right فارغة، وهو **الأصل نفسه** لا عقداً. بقية الأسطر عقود.

حقول الإغريق و`implied_volatility` موجودة في الصيغة لكن تجربة v1 لا تستخدمها (`useGreeks` و`useImpliedVolatility` كلاهما `false`).

## 4. متطلبات مصاحبة

- `data/equity/usa/map_files/` و`factor_files/` — غيابها يوقف تقدّم المحرّك مبكراً.
- `data/market-hours/market-hours-database.json` و`data/symbol-properties/symbol-properties-database.csv` — ينسخهما سكربتا Smoke وBacktest تلقائياً عند غيابهما.
- `data/alternative/interest-rate/usa/interest-rate.csv` — ظهر في الطلبات الفاشلة بعد بذر `map_files`.

## 5. ما لا تفعله

**لا تولّد أسعاراً.** `invalid-data` نتيجة صحيحة؛ أما بيانات مصطنعة في مجلد تشغيل فلا يمكن تمييزها لاحقاً عن نتيجة حقيقية. المزوّد المصطنع مرفوض صراحةً في `OptionsLabLiveDataQueue`، ولا يوجد علم يفعّله.
