const { AzureKeyCredential, DocumentAnalysisClient } = require("@azure/ai-form-recognizer");
const fs = require("fs");

// Azure Document Intelligence Configuration
const AZURE_ENDPOINT = process.env.AZURE_ENDPOINT || 'https://documentintelligencesvc.cognitiveservices.azure.com';
const AZURE_API_KEY = process.env.AZURE_API_KEY || '2APsuid45909cAF4CrXr8ARjQDfzDGJXmJLsO2i4QLtuHFMDQ1nGJQQJ99BAACYeBjFXJ3w3AAALACOGHC2Y';
const MODEL_ID = "prebuilt-read";

const client = new DocumentAnalysisClient(AZURE_ENDPOINT, new AzureKeyCredential(AZURE_API_KEY));

// Validate document quality
const validateDocumentQuality = (analysisResult) => {
    if (analysisResult?.status !== 'succeeded') {
        return { valid: false, reason: "Document processing failed" };
    }
    
    if (analysisResult?.errors?.length) {
        return { valid: false, reason: "Document appears blurry or unreadable" };
    }

    return { valid: true };
};

// Validate security features
const validateSecurityFeatures = (extractedData, docType) => {
    if (docType === "DRIVER_LICENSE" && !extractedData?.securityFeaturePresent) {
        return { valid: false, reason: "Security feature missing (e.g., hologram or watermark)" };
    }
    return { valid: true };
};

// Extract relevant document data
const extractDocumentData = (analysisResult) => {
    const fields = analysisResult?.analyzeResult?.documents?.[0]?.fields || {};
    return {
        idNumber: fields?.["ID Number"]?.valueString || null,
        name: fields?.["Full Name"]?.valueString || null,
        dob: fields?.["Date of Birth"]?.valueString || null,
        issueDate: fields?.["Issue Date"]?.valueString || null,
        expiryDate: fields?.["Expiry Date"]?.valueString || null,
        securityFeaturePresent: fields?.["Security Feature"]?.valueBoolean || false
    };
};

// Function to read and process image file
const processDocumentFromFile = async (filePath, documentType) => {
    try {
        const fileBuffer = fs.readFileSync(filePath);
        console.log("Sending document to Azure AI Document Intelligence...");
        
        const poller = await client.beginAnalyzeDocument(MODEL_ID, fileBuffer);
        const analysisResult = await poller.pollUntilDone();

        const validationQuality = validateDocumentQuality(analysisResult);
        if (!validationQuality.valid) {
            throw new Error(validationQuality.reason);
        }

        const extractedData = extractDocumentData(analysisResult);
        const validationSecurity = validateSecurityFeatures(extractedData, documentType);

        if (!validationSecurity.valid) {
            throw new Error(validationSecurity.reason);
        }

        console.log("Document processed successfully", extractedData);
        return extractedData;
    } catch (error) {
        console.error("Azure AI Document Intelligence error: ", error);
        return { error: error.message };
    }
};

// Sample usage with a local image file
const sampleFilePath = "C:\\temp\\TZ_V.png";
processDocumentFromFile(sampleFilePath, "DRIVER_LICENSE");
