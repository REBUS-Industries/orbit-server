FROM node:22-alpine
WORKDIR /orbit-docs

COPY docs-server/package.json ./
RUN npm install --omit=dev --no-audit --no-fund

COPY docs-server/server.mjs ./
COPY docs/api ./content

ENV NODE_ENV=production
ENV PORT=3080
ENV DOCS_DIR=/orbit-docs/content
ENV PUBLIC_BASE_URL=https://orbit.rebus.industries

EXPOSE 3080
CMD ["node", "server.mjs"]
