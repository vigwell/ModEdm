const AWS = require('aws-sdk');
const s3 = new AWS.S3();
const bucketName = process.env.BUCKET_NAME;

exports.handler = async (event) => {

    

    if (event.body) {
        event = JSON.parse(event.body);
    }

    if (event.action === "getZipFilesToProceed") {
        return await getZipFilesToProceed(event);
    }

    if (event.action === "getFileCaption") {
        return await getFileCaption(event);
    }

    if (event.action === "uploadMetaFile") {
        return await uploadMetaFile(event);
    }

    if (event.action === "downloadFile") {
        return await downloadFile(event);
    }

    return await doDefaultProcessing(event);
    
};

async function doDefaultProcessing(event) {
    return { success: true, payload: { result: "Invalid action" } };
}

async function getZipFilesToProceed(event) {
    try {
        const { Contents } = await s3.listObjectsV2({ Bucket: bucketName }).promise();
        const zipFiles = Contents.filter(file => file.Key.endsWith('.zip'));

        let filesToProcess;
        if (event.getOnlyNewZipFiles ) {
            const jsonFiles = new Set(
                Contents.filter(file => file.Key.endsWith('.json'))
                    .map(file => file.Key.replace('.json', ''))
            );
            filesToProcess = zipFiles.filter(zip => !jsonFiles.has(zip.Key.replace('.zip', '')));
        } else {
            filesToProcess = zipFiles;
        }

        return { success: true, payload: { result: filesToProcess.map(file => file.Key) } };
    } catch (error) {
        return { success: false, payload: { result: error.message } };
    }
}

async function getFileCaption(event) {
    try {
        
        const lambda = new AWS.Lambda();

        const { fileBuffer, fileName } = event;
        let metadata = { fileName, fileCaption: fileName };


        const ocrResponse = await lambda.invoke({
            FunctionName: "ModEdmOCR",
            Payload: JSON.stringify({ action: "extractTextFromBuffer", base64String: fileBuffer, fileName: fileName })
        }).promise();

        const ocrResult = JSON.parse(ocrResponse.Payload);
        if (!ocrResult.success) {
            return { success: false, payload: metadata, warning: "OCR failed" };
        }
        const extractedText = ocrResult.payload.result || '';

        if (extractedText==='') 
            return { success: false, payload: metadata, warning: "OCR returned null" };

        const aiResponse = await lambda.invoke({
            FunctionName: "ModEdmAI",
            Payload: JSON.stringify({ action: "analyzeText", inputText: extractedText })
        }).promise();

        const aiResult = JSON.parse(aiResponse.Payload);
        if (!aiResult.success) {
            return { success: false, payload: metadata, warning: "AI analysis failed" };
        }

        metadata.fileCaption = aiResult.payload.result || fileName;
        return { success: true, payload: metadata };
    } catch (error) {
        return { success: false, payload: { result: error.message } };
    }
}

async function uploadMetaFile(event) {
    try {
        const { fileName, fileContent, overwrite = true } = event;
        const fileNameWithJson = fileName.includes('.') ? fileName.replace(/\.[^/.]+$/, '.json') : `${fileName}.json`;

        if (!overwrite) {
            const headParams = { Bucket: bucketName, Key: fileNameWithJson };
            try {
                await s3.headObject(headParams).promise();
                return { success: false, payload: { result: "File already exists and overwrite is set to false" } };
            } catch (err) {
                if (err.code !== 'NotFound') {
                    throw err; 
                }
            }
        }

        await s3.putObject({
            Bucket: bucketName,
            Key: fileNameWithJson,
            Body: fileContent,
            ContentType: "application/json"
        }).promise();

        return { success: true, payload: { result: "Metadata file uploaded successfully" } };
    } catch (error) {
        return { success: false, payload: { result: error.message } };
    }
}

async function downloadFile(event) {
    try {
        const { fileName } = event;
        const s3Response = await s3.getObject({ Bucket: bucketName, Key: fileName }).promise();
        const base64Data = s3Response.Body.toString('base64');

        return { success: true, payload: { result: base64Data } };
    } catch (error) {
        return { success: false, payload: { result: error.message } };
    }
}
