const fs = require('fs');
const pdf2img = require('pdf-poppler'); // Converts PDF to images
const Tesseract = require('tesseract.js');
const path = require('path');

const pdfPath = "sample1.pdf"; // Path to your PDF file
const outputDir = "/tmp/"; // Directory for temporary images

async function extractTextFromScannedPDF(pdfPath, outputDir) {
    try {
        // Convert PDF to images
        const opts = {
            format: "jpeg",
            out_dir: outputDir,
            out_prefix: "page",
            dpi: 300
        };
        await pdf2img.convert(pdfPath, opts);

        // Get generated image paths
        const images = fs.readdirSync(outputDir)
            .filter(file => file.startsWith("page") && file.endsWith(".jpg"))
            .map(file => path.join(outputDir, file));

        if (images.length === 0) {
            throw new Error("No images were generated from the PDF.");
        }

        console.log("✅ PDF converted to images:", images);

        let extractedText = "";
        for (const imagePath of images) {
            console.log("🔍 Running OCR on:", imagePath);
            const { data: { text } } = await Tesseract.recognize(imagePath, "eng+heb");
            extractedText += text + "\n";
        }

        console.log("✅ Extracted Text:\n", extractedText);
        return extractedText;
    } catch (error) {
        console.error("❌ Error processing scanned PDF:", error);
    }
}

// Run OCR on the scanned PDF
extractTextFromScannedPDF(pdfPath, outputDir);
