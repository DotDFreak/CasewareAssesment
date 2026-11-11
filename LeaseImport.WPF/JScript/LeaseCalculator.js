function calculateLeases(jsonText, answers) {

    var parsedData = JsonParser.parse(jsonText);

    var input = {
        Framework: answers.Framework,
        EntitySize: answers.EntitySize,
        HasSubleases: answers.HasSubleases,
        Leases: parsedData.Leases
    };

    var leases = input.Leases;

    // F-01: Count accepted leases.
    var leaseCount = leases.length;

    // F-02: Are there any leases?
    var hasLeases = leaseCount > 0 ? "Yes" : "No";

    // F-03: Do leases have different currencies?
    var mixedCurrency = "No";

    if (leaseCount > 1) {
        var firstCurrency = leases[0].Currency;

        for (var i = 1; i < leaseCount; i++) {
            if (leases[i].Currency != firstCurrency) {
                mixedCurrency = "Yes";
                break;
            }
        }
    }

    // F-04: Not sure about this, can't find info about KPIrequired
    var summaryRequired = null;

    // F-05: Total closing value
    var totalClosing = null;

    if (mixedCurrency == "No") {
        var total = 0;

        for (var j = 0; j < leaseCount; j++) {
            var lease = leases[j];

            var closing =
                lease.Opening +
                lease.Additions -
                lease.Depreciation -
                lease.Disposals;

            total += closing;
        }

        totalClosing = roundAmount(total);
    }

    // F-06: Average closing value
    var averageClosing = null;

    if (totalClosing !== null && leaseCount > 0) {
        averageClosing = roundAmount(totalClosing / leaseCount);
    }

    // F-07: Show ROU disclosure?
    var showRouDisclosure = hasLeases == "Yes" && input.Framework == "IFRS" ? "Yes" : "No";

    // F-08: Show maturity analysis?
    var showMaturityAnalysis = (hasLeases == "Yes" && input.Framework == "IFRS") || input.EntitySize == "Large" ? "Yes" : "No";

    // F-09: Show sublease disclosure?
    var showSubleaseDisclosure = input.HasSubleases == "Yes" && hasLeases == "No" ? "Yes" : "No";

    // F-10: Not sure about this, can;t find info about TotalCashOutflow
    var showCashOutflow = null;

    return {
        LeaseCount: leaseCount,
        HasLeases: hasLeases,
        MixedCurrency: mixedCurrency,
        SummaryRequired: summaryRequired,
        TotalClosing: totalClosing,
        AverageClosing: averageClosing,
        ShowRouDisclosure: showRouDisclosure,
        ShowMaturityAnalysis: showMaturityAnalysis,
        ShowSubleaseDisclosure: showSubleaseDisclosure,
        ShowCashOutflow: showCashOutflow
    };
}

function roundAmount(value) {
    var parts = Math.abs(value).toString().split("e");
    var exponent = parts.length > 1 ? Number(parts[1]) : 0;
    var shifted = Number(parts[0] + "e" + (exponent + 2));
    var rounded = Math.round(shifted);

    parts = rounded.toString().split("e");
    exponent = parts.length > 1 ? Number(parts[1]) : 0;
    rounded = Number(parts[0] + "e" + (exponent - 2));

    return value < 0 && rounded !== 0 ? -rounded : rounded;
}
