FROM node:24-alpine
WORKDIR /app
COPY package*.json ./
RUN npm install
COPY src ./src
EXPOSE 8090
CMD ["npm", "start"]
