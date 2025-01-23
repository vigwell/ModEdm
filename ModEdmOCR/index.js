const { S3Client, GetObjectCommand } = require("@aws-sdk/client-s3");
const Tesseract = require('tesseract.js');
const path = require('path');
const pdfParse = require('pdf-parse');

// Configuration constants
const CONFIG = {
    DEFAULT_REGION: process.env.DEFAULT_REGION || 'us-east-1',
    S3_BUCKET_NAME: process.env.S3_BUCKET_NAME,
    SUPPORTED_FORMATS: ['.jpg', '.jpeg', '.png', '.tiff', '.bmp', '.gif', '.pdf', '.webp']
};

class OCRProcessor {
    constructor() {
        this.s3Client = new S3Client({ region: CONFIG.DEFAULT_REGION });
        this.supportedFormats = CONFIG.SUPPORTED_FORMATS;
    }

    async getFileFromS3(fileKey) {
        try {
            const response = await this.s3Client.send(new GetObjectCommand({
                Bucket: CONFIG.S3_BUCKET_NAME,
                Key: fileKey
            }));
            return await response.Body.transformToString('base64');
        } catch (error) {
            console.error("Error retrieving file from S3:", error);
            throw new Error("Failed to fetch file from S3");
        }
    }

    async extractText(fileBuffer, fileName) {
        const fileExt = path.extname(fileName).toLowerCase();
        
        if (!this.supportedFormats.includes(fileExt)) {
            return { success: false, payload: { result: "Unsupported file format" } };
        }
        
        try {
            const { data: { text } } = await Tesseract.recognize(
                Buffer.from(fileBuffer, 'base64'),
                'heb+eng'
            );
            return { success: true, payload: { result: text.trim() || null } };
        } catch (error) {
            console.error("OCR processing error:", error);
            return { success: false, payload: { result: error.message } };
        }
    }

    async extractTextFromBuffer(base64String) {
        try {

            const isPdf = base64String.startsWith('JVBER'); // PDF files start with "%PDF" (Base64: JVBER)

            if (isPdf) {
                //return { success: false, payload: { result: "PDF format is not supported directly. Convert it to an image first." } };
                // Convert base64 string to buffer
                const pdfBuffer = Buffer.from(base64String, 'base64');
                // Call the extractTextFromPdf function to parse the text
                const text = await this.extractTextFromPdf(pdfBuffer);
                return { success: true, payload: { result: text.trim() || null } };
            }
           
            const { data: { text } } = await Tesseract.recognize(
                Buffer.from(base64String, 'base64'),
                'heb+eng'
            );
            const res = await this.cleanExtractedText(text.trim());
            return { success: true, payload: { result: res || null } };

        } catch (error) {
            console.error("OCR processing error:", error);
            return { success: false, payload: { result: error.message } };
        }
    }

    async extractTextFromPdf(pdfBuffer) {
        try {
            const data = await pdfParse(pdfBuffer);
            //console.log("Extracted Text:\n", data.text);
            const res = await this.cleanExtractedText(data.text);
            return res;

        } catch (error) {
            console.error("Error extracting text from PDF:", error.message);
        }
    }

    async cleanExtractedText(text) {
        if (!text || typeof text !== 'string') {
            return '';
        }
    
        // Remove non-printable characters, special symbols, and excessive spaces
        let cleanedText = text
            .replace(/[^\x20-\x7E\u0590-\u05FF]/g, '')  // Remove non-ASCII except Hebrew
            .replace(/[\u200B-\u200F\u202A-\u202E]/g, '')  // Remove invisible Unicode characters
            .replace(/_{2,}/g, '')  // Remove lines of underscores (5 or more consecutive)
            .replace(//g, '')  // Remove specific unwanted characters
            .replace(/\s{2,}/g, ' ')  // Replace multiple spaces with a single space
            .replace('__', '')
            .trim();  // Trim leading and trailing spaces
    
        return cleanedText;
    }
}

exports.handler = async (event) => {
    console.log("Received event:", JSON.stringify(event, null, 2));

    const ocrProcessor = new OCRProcessor();

    if (event.action === "extractTextS3File") {
        try {
            const fileBuffer = await ocrProcessor.getFileFromS3(event.fileKey);
            return await ocrProcessor.extractText(fileBuffer, event.fileName);
        } catch (error) {
            console.error(error)
            return { success: false, payload: { result: '' } };
        }
    } else if (event.action === "extractTextFromBuffer") {
        try {
            
            return await ocrProcessor.extractTextFromBuffer(event.base64String);
        } catch (error) {
            console.error(error)
            return { success: false, payload: { result: '' } };
        }
    } else {
        return { success: false, payload: { result: "Invalid action or missing event data" } };
    }
};
