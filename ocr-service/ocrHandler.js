const fs = require("fs-extra");
const path = require("path");
const Tesseract = require("tesseract.js");
const pdf2img = require("pdf-poppler");
const os = require("os");

const TMP_DIR = path.join(os.homedir(), "tmp");

async function extractTextFromPDF(base64String) {
    try {
        const isPdf = base64String.startsWith("JVBER");
        if (!isPdf) {
            throw new Error("Provided input is not a valid PDF file.");
        }

        const pdfBuffer = Buffer.from(base64String, "base64");
        const pdfPath = path.join(TMP_DIR, "input.pdf");
        const outputDir = path.join(TMP_DIR, "images");

        await fs.ensureDir(TMP_DIR);
        await fs.ensureDir(outputDir);

        await fs.writeFile(pdfPath, pdfBuffer);
        await convertPDFToImages(pdfPath, outputDir);
        const extractedText = await extractTextFromImages(outputDir);

        return extractedText.trim();
    } catch (error) {
        throw new Error("OCR Processing Failed: " + error.message);
    }
}

async function convertPDFToImages(pdfPath, outputDir) {
    try {
        await fs.ensureDir(outputDir);
        const opts = {
            format: "jpeg",
            out_dir: outputDir,
            out_prefix: "page",
            dpi: 300,
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

        return leanExtractedText(extractedText.trim());
    } catch (error) {
        throw new Error("Failed to extract text from images: " + error.message);
    }
}

async function leanExtractedText(text) {
    if (!text || typeof text !== 'string') {
        return '';
    }

    let cleanedText = text
        .replace(/[^\x20-\x7E\u0590-\u05FF]/g, '')  
        .replace(/[\u200B-\u200F\u202A-\u202E]/g, '')  
        .replace(/_{2,}/g, '')  
        .replace(//g, '')  
        .replace(/\s{2,}/g, ' ')  
        .replace('__', '')
        .trim();

    return cleanedText;
}

// ✅ Make sure to export correctly
module.exports = {
    extractTextFromPDF,
};
