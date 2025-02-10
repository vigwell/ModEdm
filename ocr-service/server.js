const express = require("express");
const bodyParser = require("body-parser");
const { extractTextFromPDF } = require("./ocrHandler");
// ec2-18-196-208-244.eu-central-1.compute.amazonaws.com

// Administrator
// @xGq?S2aMN!C8Nd*WL)MOD0GnfOiqDh4

const app = express();
const PORT = 3000; // Change this if needed

app.use(bodyParser.json({ limit: "50mb" })); // Support large files

// Define the OCR route
app.post("/extract-text", async (req, res) => {
    try {
        const { base64String } = req.body;

        if (!base64String) {
            return res.status(400).json({ success: false, error: "Missing base64String in request." });
        }

        console.log("🔄 Processing OCR request...");

        // Run OCR extraction
        const extractedText = await extractTextFromPDF(base64String);

        console.log("✅ OCR Extraction Complete!");
        res.status(200).json({ success: true, result: extractedText.trim() || null });
    } catch (error) {
        console.error("❌ Error:", error);
        res.status(500).json({ success: false, error: error.message });
    }
});

// Start Express server
app.listen(PORT, () => {
    console.log(`🚀 Server running on http://localhost:${PORT}`);
});
