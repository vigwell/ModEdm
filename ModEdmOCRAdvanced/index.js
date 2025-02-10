const fs = require("fs-extra");
const path = require("path");
const Tesseract = require("tesseract.js");
const pdf2img = require("pdf-poppler"); // Converts PDF to images

const TMP_DIR = "/tmp"; // AWS Lambda writable storage

exports.lambdaHandler = async (event) => {
    try {
        console.log("🔄 Starting extractTextFromBufferPoppler Lambda...");

        // Validate input
        if (!event.base64String) {
            throw new Error("Missing base64String in request.");
        }

        const isPdf = event.base64String.startsWith("JVBER");
        if (!isPdf) {
            throw new Error("Provided input is not a valid PDF file.");
        }

        const pdfBuffer = Buffer.from(event.base64String, "base64");
        const pdfPath = path.join(TMP_DIR, "input.pdf");
        const outputDir = path.join(TMP_DIR, "images");

        // Write PDF to /tmp
        await fs.writeFile(pdfPath, pdfBuffer);

        // Convert PDF to images using pdf-poppler
        await convertPDFToImages(pdfPath, outputDir);

        // Extract text from images using Tesseract
        const extractedText = await extractTextFromImages(outputDir);

        console.log("✅ OCR Extraction Complete!");

        return {
            statusCode: 200,
            body: JSON.stringify({ success: true, result: extractedText.trim() || null }),
        };
    } catch (error) {
        console.error("❌ Error:", error);
        return {
            statusCode: 500,
            body: JSON.stringify({ success: false, error: error.message }),
        };
    }
};

async function convertPDFToImages(pdfPath, outputDir) {
    try {
        await fs.ensureDir(outputDir);

        const opts = {
            format: "jpeg",
            out_dir: outputDir,
            out_prefix: "page",
            dpi: 300, // Higher DPI for better OCR accuracy
        };

        console.log("🛠️ Converting PDF to images...");
        await pdf2img.convert(pdfPath, opts);
        console.log("✅ PDF converted to images.");
    } catch (error) {
        throw new Error("Failed to convert PDF to images: " + error.message);
    }
}

async function extractTextFromImages(outputDir) {
    try {
        const imageFiles = (await fs.readdir(outputDir))
            .filter((file) => file.startsWith("page") && file.endsWith(".jpg"))
            .map((file) => path.join(outputDir, file));

        if (imageFiles.length === 0) {
            throw new Error("No images found after conversion.");
        }

        console.log("🔍 Extracting text from images...");

        let extractedText = "";
        for (const imagePath of imageFiles) {
            console.log("📄 Processing:", imagePath);
            const { data: { text } } = await Tesseract.recognize(imagePath, "eng+heb");
            extractedText += text + "\n";
        }

        return extractedText.trim();
    } catch (error) {
        throw new Error("Failed to extract text from images: " + error.message);
    }
}
