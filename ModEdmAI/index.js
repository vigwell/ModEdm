const { BedrockRuntimeClient, InvokeModelCommand } = require("@aws-sdk/client-bedrock-runtime");

// Configuration constants
const CONFIG = {
    DEFAULT_REGION: 'eu-central-1',
    DEFAULT_MODEL_ID: 'anthropic.claude-3-sonnet-20240229-v1:0',
    DEFAULT_MAX_TOKENS: 1000
};

class BedrockAI {
    constructor() {
        this.region = process.env.AWS_BEDROCK_REGION || CONFIG.DEFAULT_REGION;
        this.modelId = process.env.AWS_BEDROCK_MODEL_ID || CONFIG.DEFAULT_MODEL_ID;
        this.maxTokens = parseInt(process.env.AWS_BEDROCK_MAX_TOKENS, 10) || CONFIG.DEFAULT_MAX_TOKENS;
        this.bedrock = new BedrockRuntimeClient({ region: this.region });
    }

    async analyzeText(inputText) {
        if (!process.env.AI_PROMPT_GETCAPTION) {
            throw new Error("Environment variable AI_PROMPT_GETCAPTION is not set.");
        }

        const prompt = process.env.AI_PROMPT_GETCAPTION
            .replace("@FILE_CONTENT@", inputText?.trim() || '')
            .replace(/\n/g, ' '); // Replace all newlines, not just the first one

        console.log('Using configuration:', {
            region: this.region,
            modelId: this.modelId,
            maxTokens: this.maxTokens,
            promptLength: prompt.length
        });

        const body = {
            anthropic_version: "bedrock-2023-05-31",
            max_tokens: this.maxTokens,
            messages: [
                {
                    role: "user",
                    content: [
                        {
                            type: "text",
                            text: prompt
                        }
                    ]
                }
            ]
        };

        return await this.invokeModel(body);
    }

    async analyzeImage(base64String) {
        if (!base64String) {
            throw new Error("base64String is required");
        }

        if (!process.env.AI_PROMPT_GETCAPTION) {
            throw new Error("Environment variable AI_PROMPT_GETCAPTION is not set.");
        }

        const prompt = process.env.AI_PROMPT_GETCAPTION
            .replace("@FILE_CONTENT@", "מצורף");

        // Ensure we have clean base64 data
        const base64Data = base64String.includes('base64,')
            ? base64String.split('base64,')[1]
            : base64String;

        // Remove any whitespace or non-ASCII characters
        const cleanBase64 = base64Data.replace(/[^A-Za-z0-9+/=]/g, '');

        const body = {
            anthropic_version: "bedrock-2023-05-31",
            max_tokens: this.maxTokens,
            messages: [
                {
                    role: "user",
                    content: [
                        {
                            type: "image",
                            source: {
                                type: "base64",
                                media_type: "image/jpeg",
                                data: cleanBase64 // Use the cleaned base64 data
                            }
                        },
                        {
                            type: "text",
                            text: prompt
                        }
                    ]
                }
            ]
        };

        return await this.invokeModel(body);
    }

    async invokeModel(body) {
        try {
            const params = {
                modelId: this.modelId,
                body: JSON.stringify(body),
                contentType: "application/json",
                accept: "application/json",
            };

            console.log('Request body:', JSON.stringify(body, null, 2));

            const command = new InvokeModelCommand(params);
            const response = await this.bedrock.send(command);
            const responseBody = JSON.parse(new TextDecoder().decode(response.body));
            
            console.log('AI Response:', JSON.stringify(responseBody, null, 2));

            const result = responseBody?.content?.[0]?.text?.trim() || '';
            return {
                success: true,
                payload: {
                    result: result
                }
            };
        } catch (error) {
            console.error("Bedrock API error:", error);
            return {
                success: false,
                payload: {
                    result: '',
                    error: error.message,
                    errorType: error.name
                }
            };
        }
    }
}

exports.handler = async (event) => {
    console.log("Received event:", JSON.stringify(event, null, 2));
    console.log("action:", event.action);

    const bedrockAI = new BedrockAI();

    // Fix the comparison operators and add proper case handling
    switch (event.action) {
        case 'analyzeText':
            return await bedrockAI.analyzeText(event.inputText?.replace(/\n/g, ' '));
        case 'analyzeImage':
            return await bedrockAI.analyzeImage(event.base64String);
        default:
            return { success: false, payload: { result: "Unsupported action" } };
    }
};