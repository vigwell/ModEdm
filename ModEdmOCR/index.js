const { S3Client, GetObjectCommand } = require("@aws-sdk/client-s3");
const Tesseract = require('tesseract.js');
const path = require('path');
const pdfParse = require('pdf-parse');
const { AzureKeyCredential, DocumentAnalysisClient } = require("@azure/ai-form-recognizer");
const axios = require('axios');

// Configuration constants
const CONFIG = {
    DEFAULT_REGION: process.env.DEFAULT_REGION || 'us-east-1',
    S3_BUCKET_NAME: process.env.S3_BUCKET_NAME,
    SUPPORTED_FORMATS: ['.jpg', '.jpeg', '.png', '.tiff', '.bmp', '.gif', '.pdf', '.webp'],
    AZURE_ENDPOINT: process.env.AZURE_ENDPOINT,  // Set this in your Lambda environment variables
    AZURE_API_KEY: process.env.AZURE_API_KEY,     // Set this in your Lambda environment variables,
    USE_AZURE_DOC_INTELIGENCE : process.env.USE_AZURE_DOC_INTELIGENCE || false,
    OCR_EC2_URL: process.env.OCR_EC2_URL || 'http://3.66.156.42:3000/extract-text'
};

class OCRProcessor {
    constructor() {
        this.s3Client = new S3Client({ region: CONFIG.DEFAULT_REGION });
        this.supportedFormats = CONFIG.SUPPORTED_FORMATS;
        this.azureClient = new DocumentAnalysisClient(
            CONFIG.AZURE_ENDPOINT, 
            new AzureKeyCredential(CONFIG.AZURE_API_KEY)
        );
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

    async extractTextFromBuffer(event) {
        try {

            let base64String = event.base64String;
            //if (CONFIG.USE_AZURE_DOC_INTELIGENCE)
            //    return await this.extractTextUsingDocumentIntelligence(base64String);

            const isPdf = base64String.startsWith('JVBER');
            if (isPdf) {
                return await this.extractTextFromPdfOnEc2(event);
                //const pdfBuffer = Buffer.from(base64String, 'base64');
                //const text = await this.extractTextFromPdf(pdfBuffer);
                //return { success: true, payload: { result: text.trim() || null } };
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

    async extractTextUsingDocumentIntelligence(base64String) {
        try {
            const fileBuffer = Buffer.from(base64String, 'base64');
            
            console.log("Sending document to Azure AI Document Intelligence...");
            const poller = await this.azureClient.beginAnalyzeDocument("prebuilt-read", fileBuffer);
            const result = await poller.pollUntilDone();

            if (!result || !result.content) {
                throw new Error("Azure AI Document Intelligence did not return any text.");
            }

            const text = result.content.trim();
            const cleanedText = await this.cleanExtractedText(text);

            return { success: true, payload: { result: cleanedText || null } };
        } catch (error) {
            console.error("Azure AI Document Intelligence error:", error);
            return { success: false, payload: { result: error.message } };
        }
    }

    async extractTextFromPdf(pdfBuffer) {
        try {
            const data = await pdfParse(pdfBuffer);
            const res = await this.cleanExtractedText(data.text);
            return res;
        } catch (error) {
            console.error("Error extracting text from PDF:", error.message);
        }
    }

    
    async extractTextFromPdfOnEc2(event) {
        try {
            let data = event;
    
            let config = {
                method: 'post',
                maxBodyLength: Infinity,
                url: CONFIG.OCR_EC2_URL,
                headers: { 
                    'Content-Type': 'application/json'
                },
                data: data
            };
            
            console.log('in extractTextFromPdfOnEc2');
            // Make the request
            const response = await axios.request(config);
    
            // Validate and process the response
            if (response.data && response.data.success) {
                const text = response.data.result;
    
                // Ensure cleanExtractedText is awaited correctly
                const cleanedText = text ? await this.cleanExtractedText(text) : null;
    
                return { success: true, payload: { result: cleanedText || null } };
            } else {
                return { success: false, payload: { result: null } };
            }
        } catch (error) {
            console.error("Error extracting text from PDF:", error.message);
    
            return { success: false, payload: { result: null } };
        }
    }
    
    
    async cleanExtractedText(text) {
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
}

exports.handler = async (event) => {
    console.log("Received event:", JSON.stringify(event, null, 2));

    const ocrProcessor = new OCRProcessor();

    if (event.action === "extractTextS3File") {
        try {
            const fileBuffer = await ocrProcessor.getFileFromS3(event.fileKey);
            return await ocrProcessor.extractText(fileBuffer, event.fileName);
        } catch (error) {
            console.error(error);
            return { success: false, payload: { result: '' } };
        }
    } else if (event.action === "extractTextFromBuffer") {
        try {
            return await ocrProcessor.extractTextFromBuffer(event);
        } catch (error) {
            console.error(error);
            return { success: false, payload: { result: '' } };
        }
    } else if (event.action === "extractTextFromBufferDI") {
        try {
            return await ocrProcessor.extractTextFromBuffer(event);
            //return await ocrProcessor.extractTextUsingDocumentIntelligence(event.base64String);
        } catch (error) {
            console.error(error);
            return { success: false, payload: { result: '' } };
        }
    } else {
        return { success: false, payload: { result: "Invalid action or missing event data" } };
    }
};
