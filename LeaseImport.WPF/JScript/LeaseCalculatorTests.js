const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const calculatorSource = fs.readFileSync(path.join(__dirname, "LeaseCalculator.js"), "utf8");

function calculate(jsonText, answers) {
    const context = vm.createContext({ JsonParser: { parse: JSON.parse } });
    vm.runInContext(calculatorSource, context, { filename: "LeaseCalculator.js" });
    // Copy the returned primitive fields into a plain object in this context.
    return { ...context.calculateLeases(jsonText, answers) };
}

const defaultAnswers = {
    Framework: "IFRS",
    EntitySize: "Medium",
    HasSubleases: "No"
};

const oneLeaseJson = `{
    "Leases": [{
        "LeaseId": "L-1",
        "Currency": "DKK",
        "Opening": 1000,
        "Additions": 125.50,
        "Depreciation": 200.25,
        "Disposals": 50.25
    }],
    "Issues": []
}`;

const multipleLeasesJson = `{
    "Leases": [
        { "LeaseId": "L-1", "Currency": "DKK", "Opening": 1000, "Additions": 100, "Depreciation": 200, "Disposals": 25 },
        { "LeaseId": "L-2", "Currency": "DKK", "Opening": 500, "Additions": 50, "Depreciation": 75, "Disposals": 10 },
        { "LeaseId": "L-3", "Currency": "DKK", "Opening": 325.05, "Additions": 10, "Depreciation": 25.05, "Disposals": 10 }
    ],
    "Issues": [{ "LineNumber": 5, "Field": "Opening", "Message": "Rejected row." }]
}`;

const emptyJson = '{ "Leases": [], "Issues": [] }';

test("one lease returns all ten fields, including additions and disposals", () => {
    assert.deepEqual(calculate(oneLeaseJson, defaultAnswers), {
        LeaseCount: 1,
        HasLeases: "Yes",
        MixedCurrency: "No",
        SummaryRequired: null,
        TotalClosing: 875,
        AverageClosing: 875,
        ShowRouDisclosure: "Yes",
        ShowMaturityAnalysis: "Yes",
        ShowSubleaseDisclosure: "No",
        ShowCashOutflow: null
    });
});

test("several leases are summed and averaged, without counting rejected rows", () => {
    const result = calculate(multipleLeasesJson, defaultAnswers);
    assert.equal(result.LeaseCount, 3);
    assert.equal(result.TotalClosing, 1640);
    assert.equal(result.AverageClosing, 546.67);
});

test("mixed currencies leave total and average closing unavailable", () => {
    const json = `{
        "Leases": [
            { "Currency": "DKK", "Opening": 1000, "Additions": 0, "Depreciation": 100, "Disposals": 0 },
            { "Currency": "EUR", "Opening": 500, "Additions": 0, "Depreciation": 50, "Disposals": 0 }
        ],
        "Issues": []
    }`;
    const result = calculate(json, defaultAnswers);
    assert.equal(result.LeaseCount, 2);
    assert.equal(result.HasLeases, "Yes");
    assert.equal(result.MixedCurrency, "Yes");
    assert.equal(result.TotalClosing, null);
    assert.equal(result.AverageClosing, null);
});

test("no leases returns zero total, unavailable average, and skipped subleases", () => {
    assert.deepEqual(calculate(emptyJson, { ...defaultAnswers, HasSubleases: "" }), {
        LeaseCount: 0,
        HasLeases: "No",
        MixedCurrency: "No",
        SummaryRequired: null,
        TotalClosing: 0,
        AverageClosing: null,
        ShowRouDisclosure: "No",
        ShowMaturityAnalysis: "No",
        ShowSubleaseDisclosure: "No",
        ShowCashOutflow: null
    });
});

for (const [framework, size, expectedRou, expectedMaturity] of [
    ["IFRS", "Micro", "Yes", "Yes"],
    ["IFRS", "Small", "Yes", "Yes"],
    ["IFRS", "Medium", "Yes", "Yes"],
    ["IFRS", "Large", "Yes", "Yes"],
    ["LocalGAAP", "Micro", "No", "No"],
    ["LocalGAAP", "Small", "No", "No"],
    ["LocalGAAP", "Medium", "No", "No"],
    ["LocalGAAP", "Large", "No", "Yes"]
]) {
    test(`${framework}, ${size}: disclosure fields follow the selected answers`, () => {
        const result = calculate(oneLeaseJson, {
            Framework: framework,
            EntitySize: size,
            HasSubleases: "No"
        });
        assert.equal(result.ShowRouDisclosure, expectedRou);
        assert.equal(result.ShowMaturityAnalysis, expectedMaturity);
    });
}

test("a large entity shows maturity analysis even without leases, using the literal OR condition", () => {
    const result = calculate(emptyJson, {
        Framework: "LocalGAAP",
        EntitySize: "Large",
        HasSubleases: ""
    });
    assert.equal(result.ShowRouDisclosure, "No");
    assert.equal(result.ShowMaturityAnalysis, "Yes");
});

test("subleases with accepted leases does not meet the literal F-09 condition", () => {
    const result = calculate(oneLeaseJson, { ...defaultAnswers, HasSubleases: "Yes" });
    assert.equal(result.ShowSubleaseDisclosure, "No");
});

test("contradictory Yes subleases with no leases meets the literal F-09 condition", () => {
    const result = calculate(emptyJson, { ...defaultAnswers, HasSubleases: "Yes" });
    assert.equal(result.ShowSubleaseDisclosure, "Yes");
});

for (const [amount, expected] of [
    ["1.005", 1.01],
    ["-1.005", -1.01],
    ["1.015", 1.02],
    ["-1.015", -1.02],
    ["1.125", 1.13],
    ["-1.125", -1.13],
    ["1.0049999", 1],
    ["-1.0049999", -1],
    ["1.0050001", 1.01],
    ["-1.0050001", -1.01],
    ["0", 0],
    ["-0.004", 0],
    ["1e-7", 0],
    ["-1e-7", 0]
]) {
    test(`closing amount ${amount} rounds to ${expected}`, () => {
        const json = `{
            "Leases": [{ "Currency": "DKK", "Opening": ${amount}, "Additions": 0, "Depreciation": 0, "Disposals": 0 }],
            "Issues": []
        }`;
        const result = calculate(json, defaultAnswers);
        assert.equal(result.TotalClosing, expected);
        assert.equal(result.AverageClosing, expected);
    });
}

for (const [total, expectedAverage] of [["2.01", 1.01], ["-2.01", -1.01]]) {
    test(`average of total ${total} over two leases rounds away from zero`, () => {
        const json = `{
            "Leases": [
                { "Currency": "DKK", "Opening": ${total}, "Additions": 0, "Depreciation": 0, "Disposals": 0 },
                { "Currency": "DKK", "Opening": 0, "Additions": 0, "Depreciation": 0, "Disposals": 0 }
            ],
            "Issues": []
        }`;
        assert.equal(calculate(json, defaultAnswers).AverageClosing, expectedAverage);
    });
}

test("fractional-cent amounts are summed before rounding", () => {
    const json = `{
        "Leases": [
            { "Currency": "DKK", "Opening": 0.004, "Additions": 0, "Depreciation": 0, "Disposals": 0 },
            { "Currency": "DKK", "Opening": 0.004, "Additions": 0, "Depreciation": 0, "Disposals": 0 }
        ],
        "Issues": []
    }`;
    assert.equal(calculate(json, defaultAnswers).TotalClosing, 0.01);
});

test("floating-point residue after additions and subtraction rounds to zero", () => {
    const json = `{
        "Leases": [{ "Currency": "DKK", "Opening": 0.1, "Additions": 0.2, "Depreciation": 0.3, "Disposals": 0 }],
        "Issues": []
    }`;
    const result = calculate(json, defaultAnswers);
    assert.equal(result.TotalClosing, 0);
    assert.equal(result.AverageClosing, 0);
});

test("malformed JSON produces a parsing error", () => {
    assert.throws(() => calculate('{ "Leases": [', defaultAnswers), { name: "SyntaxError" });
});
