// JavaScript source code
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

// Change these leases to try different calculation inputs.
const importResult = {
    Leases: [
        {
            LeaseId: "L-2001",
            AssetClass: "Property",
            Description: "Office Aarhus",
            Currency: "DKK",
            Opening: 1000,
            Additions: 100,
            Depreciation: 200,
            Disposals: 0
        },
        {
            LeaseId: "L-2002",
            AssetClass: "Equipment",
            Description: "Printer",
            Currency: "DKK",
            Opening: 500,
            Additions: 0,
            Depreciation: 50,
            Disposals: 0
        }
    ],
    Issues: []
};

const answers = {
    Framework: "IFRS",       // IFRS or LocalGAAP
    EntitySize: "Medium",    // Micro, Small, Medium, or Large
    HasSubleases: "No"       // Yes, No, or "" when there are no leases
};

// Provide the parser expected by LeaseCalculator.js, then load its functions.
const context = vm.createContext({
    JsonParser: { parse: JSON.parse }
});
const scriptPath = path.join(__dirname, "JScript", "LeaseCalculator.js");
vm.runInContext(fs.readFileSync(scriptPath, "utf8"), context, {
    filename: "LeaseCalculator.js"
});

const result = context.calculateLeases(JSON.stringify(importResult), answers);
console.log(JSON.stringify(result, null, 2));
