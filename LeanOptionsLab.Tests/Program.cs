using LeanOptionsLab.Domain;

namespace LeanOptionsLab.Tests;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Body)[]
        {
            ("configuration layout accepts the documented v1 experiment", ConfigurationLayoutIsValid),
            ("configuration layout rejects a mixed evaluation window", ConfigurationRejectsMixedWindow),
            ("configuration validation keeps paper conditional and live prohibited", ConfigurationExecutionModesStayFailClosed),
            ("credit vertical payoff and maximum loss are bounded", CreditVerticalPayoffAndRisk),
            ("debit vertical payoff and maximum loss are bounded", DebitVerticalPayoffAndRisk),
            ("contract selection rejects a missing chain", ContractSelectionRejectsMissingChain),
            ("contract selection rejects a missing quote", ContractSelectionRejectsMissingQuote),
            ("contract selection rejects a missing expiry", ContractSelectionRejectsMissingExpiry),
            ("contract selection rejects unapproved rules", ContractSelectionRejectsUnapprovedRules),
            ("data request failures produce invalid data", DataRequestFailureProducesInvalidData),
            ("missing rules or costs block ranking", MissingRulesOrCostsBlockRanking),
            ("ranking only uses out-of-sample metrics", RankingUsesOnlyOutOfSampleMetrics),
            ("identical top metrics produce no synthetic winner", TiedTopMetricsBlockRanking),
            ("report status tokens preserve hyphenated contract values", ReportStatusTokensAreExact),
            ("LEAN log extractor reads only explicit lifecycle markers", LeanLogExtractorReadsExplicitMarkers),
            ("report writer emits JSON and Arabic Markdown", ReportWriterEmitsAuditableArtifacts),
            ("paper readiness blocks the current unfounded configuration", PaperReadinessBlocksUnfoundedRun),
            ("paper readiness requires every barrier, not a subset", PaperReadinessRequiresEveryBarrier),
            ("paper readiness clears only when all barriers are met", PaperReadinessClearsWhenFullyApproved),
            ("paper readiness derives rules and costs from the typed configuration", PaperReadinessUsesTypedConfiguration),
            ("paper readiness never permits live trading", PaperReadinessRejectsLiveTrading)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Body();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void ConfigurationLayoutIsValid()
    {
        var result = ExperimentConfigurationValidator.Validate(CreateConfiguration());
        AssertTrue(result.IsValid, string.Join("; ", result.Issues.Select(issue => issue.Message)));
    }

    private static void ConfigurationRejectsMixedWindow()
    {
        var configuration = CreateConfiguration(trainingEnd: new DateOnly(2024, 1, 1));
        var result = ExperimentConfigurationValidator.Validate(configuration);

        AssertTrue(!result.IsValid);
        AssertTrue(result.Issues.Any(issue => issue.Code == "training-window"));
    }

    private static void ConfigurationExecutionModesStayFailClosed()
    {
        var approvedPaper = ExperimentConfigurationValidator.Validate(
            CreateConfiguration(enablePaperTrading: true));
        AssertTrue(approvedPaper.IsValid, string.Join("; ", approvedPaper.Issues.Select(issue => issue.Message)));

        var unfoundedPaper = ExperimentConfigurationValidator.Validate(
            CreateConfiguration(approveRules: false, includeCosts: false, enablePaperTrading: true));
        AssertTrue(unfoundedPaper.Issues.Any(issue => issue.Code == "paper-readiness"));

        var live = ExperimentConfigurationValidator.Validate(
            CreateConfiguration(enableLiveTrading: true));
        AssertTrue(live.Issues.Any(issue => issue.Code == "execution-mode"));
    }

    private static void CreditVerticalPayoffAndRisk()
    {
        var risk = VerticalSpreadPayoff.CreditVerticalRisk(10m, 12m, 0.80m);

        AssertEqual(80m, risk.MaxGain);
        AssertEqual(120m, risk.MaxLoss);
        AssertEqual(80m, VerticalSpreadPayoff.CreditVerticalPayoffAtExpiry(OptionRight.Call, 10m, 12m, 0.80m, 5m));
        AssertEqual(-120m, VerticalSpreadPayoff.CreditVerticalPayoffAtExpiry(OptionRight.Call, 10m, 12m, 0.80m, 20m));
        AssertEqual(80m, VerticalSpreadPayoff.CreditVerticalPayoffAtExpiry(OptionRight.Put, 12m, 10m, 0.80m, 20m));
        AssertEqual(-120m, VerticalSpreadPayoff.CreditVerticalPayoffAtExpiry(OptionRight.Put, 12m, 10m, 0.80m, 0m));
    }

    private static void DebitVerticalPayoffAndRisk()
    {
        var risk = VerticalSpreadPayoff.DebitVerticalRisk(10m, 12m, 0.80m);

        AssertEqual(120m, risk.MaxGain);
        AssertEqual(80m, risk.MaxLoss);
        AssertEqual(-80m, VerticalSpreadPayoff.DebitVerticalPayoffAtExpiry(OptionRight.Call, 10m, 12m, 0.80m, 5m));
        AssertEqual(120m, VerticalSpreadPayoff.DebitVerticalPayoffAtExpiry(OptionRight.Call, 10m, 12m, 0.80m, 20m));
        AssertEqual(-80m, VerticalSpreadPayoff.DebitVerticalPayoffAtExpiry(OptionRight.Put, 12m, 10m, 0.80m, 20m));
        AssertEqual(120m, VerticalSpreadPayoff.DebitVerticalPayoffAtExpiry(OptionRight.Put, 12m, 10m, 0.80m, 0m));
    }

    private static void ContractSelectionRejectsMissingChain()
    {
        var decision = ContractSelectionGate.Evaluate(ApprovedSelectionRequest() with { HasOptionChain = false });

        AssertTrue(!decision.IsEligible);
        AssertEqual(ContractRejectionReason.MissingOptionChain, decision.Reason);
    }

    private static void ContractSelectionRejectsMissingQuote()
    {
        var decision = ContractSelectionGate.Evaluate(ApprovedSelectionRequest() with { Bid = null });

        AssertTrue(!decision.IsEligible);
        AssertEqual(ContractRejectionReason.MissingLegQuote, decision.Reason);
    }

    private static void ContractSelectionRejectsMissingExpiry()
    {
        var decision = ContractSelectionGate.Evaluate(ApprovedSelectionRequest() with
        {
            Expiry = null,
            DaysToExpiry = null
        });

        AssertTrue(!decision.IsEligible);
        AssertEqual(ContractRejectionReason.MissingExpiry, decision.Reason);
    }

    private static void ContractSelectionRejectsUnapprovedRules()
    {
        var decision = ContractSelectionGate.Evaluate(new ContractSelectionRequest
        {
            EntryAndExitRulesApproved = false
        });

        AssertTrue(!decision.IsEligible);
        AssertEqual(ContractRejectionReason.RulesNotApproved, decision.Reason);
    }

    private static void DataRequestFailureProducesInvalidData()
    {
        var decision = DataReadinessGate.Evaluate(new DataReadinessEvidence
        {
            EquitySecurityMasterAvailable = true,
            UnderlyingMinuteTradeAvailable = true,
            OptionMinuteTradeAvailable = true,
            OptionMinuteQuoteAvailable = true,
            DataRequestFailures = 1
        });

        AssertTrue(!decision.IsReady);
        AssertTrue(decision.Reasons.Any(reason => reason.Contains("failed data request", StringComparison.Ordinal)));
    }

    private static void MissingRulesOrCostsBlockRanking()
    {
        var configuration = CreateConfiguration(approveRules: false, includeCosts: false);
        var decision = ComparisonRankingService.Rank(configuration, CreateCompleteEvaluations());

        AssertEqual(ComparisonStatus.NotRankable, decision.Status);
        AssertTrue(decision.RankedStrategies.Count == 0);
    }

    private static void RankingUsesOnlyOutOfSampleMetrics()
    {
        var evaluations = CreateCompleteEvaluations(
            putTraining: 9m,
            putValidation: 8m,
            putOutOfSample: 0.50m,
            callTraining: 0.20m,
            callValidation: 0.30m,
            callOutOfSample: 0.80m,
            debitOutOfSample: 0.40m);
        var decision = ComparisonRankingService.Rank(CreateConfiguration(), evaluations);

        AssertEqual(ComparisonStatus.Ranked, decision.Status);
        AssertEqual("Call Credit Vertical", decision.RankedStrategies[0].TemplateName);
    }

    private static void TiedTopMetricsBlockRanking()
    {
        var evaluations = CreateCompleteEvaluations(
            putOutOfSample: 0.80m,
            callOutOfSample: 0.80m,
            debitOutOfSample: 0.40m,
            putDrawdown: 0.10m,
            callDrawdown: 0.10m);
        var decision = ComparisonRankingService.Rank(CreateConfiguration(), evaluations);

        AssertEqual(ComparisonStatus.NotRankable, decision.Status);
        AssertTrue(decision.Reasons.Single().Contains("tied", StringComparison.Ordinal));
    }

    private static void ReportWriterEmitsAuditableArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "LeanOptionsLabTests", Guid.NewGuid().ToString("N"));

        try
        {
            var report = RunReportFactory.Create(
                "unit-report",
                "test-version",
                CreateConfiguration(),
                ReadyEvidence(),
                CreateCompleteEvaluations(),
                new[]
                {
                    new RecordedLifecycleEvent
                    {
                        OccurredAtUtc = DateTimeOffset.UnixEpoch,
                        Kind = "order",
                        Detail = "no live order"
                    }
                },
                new[]
                {
                    new RecordedLifecycleEvent
                    {
                        OccurredAtUtc = DateTimeOffset.UnixEpoch,
                        Kind = "assignment",
                        Detail = "simulated assignment log"
                    }
                });
            var paths = RunReportWriter.Write(root, report);

            AssertTrue(File.Exists(paths.JsonPath));
            AssertTrue(File.Exists(paths.MarkdownPath));
            var markdown = File.ReadAllText(paths.MarkdownPath);
            AssertTrue(markdown.Contains("# تقرير مختبر الأوبشن المحلي", StringComparison.Ordinal));
            AssertTrue(markdown.Contains("ranked", StringComparison.Ordinal));
            AssertTrue(markdown.Contains("أحداث Assignment", StringComparison.Ordinal));
            AssertTrue(File.ReadAllText(paths.JsonPath).Contains("\"finalStatus\": \"ranked\"", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void ReportStatusTokensAreExact()
    {
        var json = ExperimentConfigurationJson.Serialize(new[]
        {
            ComparisonStatus.Ranked,
            ComparisonStatus.NotRankable,
            ComparisonStatus.InvalidData
        });

        AssertTrue(json.Contains("\"ranked\"", StringComparison.Ordinal));
        AssertTrue(json.Contains("\"not-rankable\"", StringComparison.Ordinal));
        AssertTrue(json.Contains("\"invalid-data\"", StringComparison.Ordinal));
    }

    private static void LeanLogExtractorReadsExplicitMarkers()
    {
        var root = Path.Combine(Path.GetTempPath(), "LeanOptionsLabTests", Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(root, "lean.log");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllLines(logPath, new[]
            {
                "unrelated engine line",
                "OPTIONS_LAB|order-event|OrderEvent 1",
                "OPTIONS_LAB|assignment-event|OrderEvent 2",
                "OPTIONS_LAB|exercise-event|OrderEvent 3"
            });

            var events = LeanLogEventExtractor.Extract(logPath);
            AssertEqual(1, events.OrderEvents.Count);
            AssertEqual(1, events.AssignmentEvents.Count);
            AssertEqual(1, events.ExerciseEvents.Count);
            AssertEqual("OrderEvent 3", events.ExerciseEvents.Single().Detail);
            AssertTrue(events.ExerciseEvents.Single().OccurredAtUtc is null);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ExperimentConfiguration CreateConfiguration(
        bool approveRules = true,
        bool includeCosts = true,
        DateOnly? trainingEnd = null,
        bool enablePaperTrading = false,
        bool enableLiveTrading = false,
        string underlying = "SPY")
    {
        StrategyRules Rules() => new()
        {
            Approved = approveRules,
            EntryRuleReference = approveRules ? "approved-entry-v1" : null,
            ExitRuleReference = approveRules ? "approved-exit-v1" : null,
            PositionSizingRuleReference = approveRules ? "approved-size-v1" : null
        };

        return new ExperimentConfiguration
        {
            Underlying = underlying,
            StrategyTemplates = new()
            {
                new() { Name = "Put Credit Vertical", Structure = "credit-vertical", Rules = Rules() },
                new() { Name = "Call Credit Vertical", Structure = "credit-vertical", Rules = Rules() },
                new() { Name = "Directional Debit Vertical", Structure = "debit-vertical", Rules = Rules() }
            },
            ExecutionCosts = new()
            {
                CommissionPerContract = includeCosts ? 0.65m : null,
                SlippagePerContract = includeCosts ? 0.05m : null,
                Source = includeCosts ? "approved-test-source" : null
            },
            EnablePaperTrading = enablePaperTrading,
            EnableLiveTrading = enableLiveTrading,
            Windows = new()
            {
                Training = new(new DateOnly(2021, 1, 1), trainingEnd ?? new DateOnly(2023, 12, 31)),
                Validation = new(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
                OutOfSample = new(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))
            }
        };
    }

    private static ContractSelectionRequest ApprovedSelectionRequest() => new()
    {
        EntryAndExitRulesApproved = true,
        HasOptionChain = true,
        HasUnderlyingQuote = true,
        Bid = 1.00m,
        Ask = 1.10m,
        Expiry = new DateOnly(2025, 2, 14),
        DaysToExpiry = 30,
        MinimumApprovedDaysToExpiry = 20,
        MaximumApprovedDaysToExpiry = 45
    };

    private static DataReadinessEvidence ReadyEvidence() => new()
    {
        EquitySecurityMasterAvailable = true,
        UnderlyingMinuteTradeAvailable = true,
        OptionMinuteTradeAvailable = true,
        OptionMinuteQuoteAvailable = true
    };

    private static List<StrategyEvaluation> CreateCompleteEvaluations(
        decimal putTraining = 0.50m,
        decimal putValidation = 0.50m,
        decimal putOutOfSample = 0.50m,
        decimal callTraining = 0.60m,
        decimal callValidation = 0.60m,
        decimal callOutOfSample = 0.60m,
        decimal debitOutOfSample = 0.40m,
        decimal putDrawdown = 0.20m,
        decimal callDrawdown = 0.20m,
        decimal debitDrawdown = 0.25m)
    {
        return new()
        {
            Evaluation("Put Credit Vertical", putTraining, putValidation, putOutOfSample, putDrawdown),
            Evaluation("Call Credit Vertical", callTraining, callValidation, callOutOfSample, callDrawdown),
            Evaluation("Directional Debit Vertical", 0.30m, 0.30m, debitOutOfSample, debitDrawdown)
        };
    }

    private static StrategyEvaluation Evaluation(
        string name,
        decimal training,
        decimal validation,
        decimal outOfSample,
        decimal drawdown) => new()
        {
            TemplateName = name,
            Training = Metrics(training, 0.30m),
            Validation = Metrics(validation, 0.25m),
            OutOfSample = Metrics(outOfSample, drawdown)
        };

    private static PeriodMetrics Metrics(decimal riskAdjustedReturn, decimal maxDrawdown) => new()
    {
        DataComplete = true,
        RiskAdjustedReturn = riskAdjustedReturn,
        MaxDrawdown = maxDrawdown
    };

    private static void PaperReadinessBlocksUnfoundedRun()
    {
        var decision = LivePaperReadinessGate.Evaluate(
            CreateConfiguration(approveRules: false, includeCosts: false),
            approvedLiveDataProviderConfigured: false,
            brokerageIsPaperOnly: false);

        AssertTrue(!decision.IsReady, "An empty evidence set must not clear the paper gate.");
        AssertEqual(5, decision.Reasons.Count);
        AssertTrue(
            decision.Reasons.Any(reason => reason.Contains("enablePaperTrading", StringComparison.Ordinal)),
            "The blocking reason must name the tracked config field.");
        AssertTrue(
            decision.Reasons.Contains(LivePaperReadinessGate.NoApprovedDataProviderReason),
            "The missing live data provider must be reported verbatim.");
    }

    private static void PaperReadinessRequiresEveryBarrier()
    {
        var mutations = new (string Barrier, ExperimentConfiguration Configuration, bool ProviderApproved, bool PaperOnly)[]
        {
            ("paper trading flag", CreateConfiguration(), true, true),
            ("live data provider", CreateConfiguration(enablePaperTrading: true), false, true),
            ("strategy rules", CreateConfiguration(approveRules: false, enablePaperTrading: true), true, true),
            ("execution costs", CreateConfiguration(includeCosts: false, enablePaperTrading: true), true, true),
            ("paper-only brokerage", CreateConfiguration(enablePaperTrading: true), true, false)
        };

        foreach (var mutation in mutations)
        {
            var decision = LivePaperReadinessGate.Evaluate(
                mutation.Configuration,
                mutation.ProviderApproved,
                mutation.PaperOnly);
            AssertTrue(!decision.IsReady, $"Dropping the {mutation.Barrier} barrier must block the paper gate.");
        }
    }

    private static void PaperReadinessClearsWhenFullyApproved()
    {
        var decision = LivePaperReadinessGate.Evaluate(
            CreateConfiguration(enablePaperTrading: true),
            approvedLiveDataProviderConfigured: true,
            brokerageIsPaperOnly: true);

        AssertTrue(decision.IsReady, string.Join("; ", decision.Reasons));
        AssertEqual(0, decision.Reasons.Count);
    }

    private static void PaperReadinessUsesTypedConfiguration()
    {
        var ready = LivePaperReadinessGate.Evaluate(
            CreateConfiguration(enablePaperTrading: true),
            approvedLiveDataProviderConfigured: true,
            brokerageIsPaperOnly: true);
        AssertTrue(ready.IsReady, string.Join("; ", ready.Reasons));

        var incomplete = LivePaperReadinessGate.Evaluate(
            CreateConfiguration(approveRules: false, includeCosts: false, enablePaperTrading: true),
            approvedLiveDataProviderConfigured: true,
            brokerageIsPaperOnly: true);
        AssertTrue(!incomplete.IsReady);
        AssertTrue(incomplete.Reasons.Any(reason => reason.StartsWith("Entry, exit", StringComparison.Ordinal)));
        AssertTrue(incomplete.Reasons.Any(reason => reason.StartsWith("Commission, slippage", StringComparison.Ordinal)));

        var emptyTemplates = LivePaperReadinessGate.Evaluate(
            new ExperimentConfiguration
            {
                EnablePaperTrading = true,
                ExecutionCosts = new()
                {
                    CommissionPerContract = 0.65m,
                    SlippagePerContract = 0.05m,
                    Source = "approved-test-source"
                }
            },
            approvedLiveDataProviderConfigured: true,
            brokerageIsPaperOnly: true);
        AssertTrue(!emptyTemplates.IsReady, "An empty strategy collection must not pass vacuously.");

        var wrongUnderlying = LivePaperReadinessGate.Evaluate(
            CreateConfiguration(enablePaperTrading: true, underlying: "QQQ"),
            approvedLiveDataProviderConfigured: true,
            brokerageIsPaperOnly: true);
        AssertTrue(!wrongUnderlying.IsReady, "An invalid experiment layout must not clear the paper gate.");
        AssertTrue(wrongUnderlying.Reasons.Any(reason => reason.Contains("[underlying]", StringComparison.Ordinal)));
    }

    private static void PaperReadinessRejectsLiveTrading()
    {
        var decision = LivePaperReadinessGate.Evaluate(
            CreateConfiguration(enablePaperTrading: true, enableLiveTrading: true),
            approvedLiveDataProviderConfigured: true,
            brokerageIsPaperOnly: true);

        AssertTrue(!decision.IsReady);
        AssertTrue(decision.Reasons.Any(reason => reason.Contains("Live trading is prohibited", StringComparison.Ordinal)));
    }

    private static void AssertTrue(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message ?? "Assertion failed.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }
}
