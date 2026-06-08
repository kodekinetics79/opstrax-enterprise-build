FROM node:20-alpine
WORKDIR /app
COPY package.json ./
# Install optional vendor SDKs (node-zklib) when present; core runs without them.
RUN npm install --omit=dev || true
COPY src ./src
# config.json is mounted at runtime: -v $(pwd)/config.json:/app/config.json
ENTRYPOINT ["node", "src/index.js"]
