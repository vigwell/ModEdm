const fs = require('fs-extra');
const path = require('path');
const { exec } = require('child_process');
const util = require('util');

const execPromise = util.promisify(exec);
const TMP_DIR = '/tmp'; // AWS Lambda's writable temp directory

async function pdfToImages(pdfBuffer) {
    const pdfPath = path.join(TMP_DIR, 'input.pdf');
    await fs.writeFile(pdfPath, pdfBuffer); // Write the PDF to temp dir

    const outputDir = path.join(TMP_DIR, 'images');
    await fs.ensureDir(outputDir);

    // Convert PDF pages to JPG using Poppler's `pdftoppm`
    const command = `pdftoppm -jpeg -r 300 "${pdfPath}" "${outputDir}/page"`;
    await execPromise(command);

    // Read all generated JPG files
    const imageFiles = (await fs.readdir(outputDir))
        .filter(file => file.endsWith('.jpg'))
        .sort((a, b) => a.localeCompare(b)); // Sort by page order

    // Convert images to base64
    const base64Images = await Promise.all(imageFiles.map(async file => {
        const imagePath = path.join(outputDir, file);
        const imageBuffer = await fs.readFile(imagePath);
        return imageBuffer.toString('base64');
    }));

    return base64Images;
}

// ✅ Fix: Properly export pdfToImages
module.exports = { pdfToImages };

// AWS Lambda Handler
exports.handler = async (event) => {
    try {
        const pdfBase64 = event.pdfBase64;
        if (!pdfBase64) throw new Error("Missing PDF base64 input");

        const pdfBuffer = Buffer.from(pdfBase64, 'base64');
        const base64Images = await pdfToImages(pdfBuffer);

        return {
            statusCode: 200,
            body: JSON.stringify({ images: base64Images })
        };
    } catch (error) {
        return {
            statusCode: 500,
            body: JSON.stringify({ error: error.message })
        };
    }
};
