# Use the official Windows Server Core image with a specific version
FROM mcr.microsoft.com/windows/nanoserver:1803-amd64

# Set the working directory
WORKDIR /app

# Set PowerShell as the default shell
SHELL ["powershell", "-Command", "$ErrorActionPreference = 'Stop';"]

# Download and install Node.js
RUN Invoke-WebRequest -Uri 'https://nodejs.org/dist/v22.0.0/node-v22.0.0-win-x64.zip' -OutFile 'node.zip'; \
    Expand-Archive -Path 'node.zip' -DestinationPath 'C:\\'; \
    Rename-Item -Path 'C:\\node-v22.0.0-win-x64' -NewName 'C:\\nodejs'; \
    Remove-Item -Path 'node.zip'; \
    [Environment]::SetEnvironmentVariable('PATH', 'C:\\nodejs;C:\\nodejs\\node_modules\\npm\\bin;' + $env:PATH, [EnvironmentVariableTarget]::Machine)

# Update PATH for the current session
ENV PATH="C:\\nodejs;C:\\nodejs\\node_modules\\npm\\bin;${PATH}"

# Copy package.json and package-lock.json
COPY package*.json ./

# Install application dependencies
RUN npm install

# Copy the rest of the application code
COPY . .

# Expose the application port
EXPOSE 3000

# Start the application
CMD ["node", "server.js"]
