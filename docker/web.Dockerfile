# Next.js 前端镜像：构建上下文为仓库根目录（docker compose 里 context: ..）。
FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

FROM node:22-alpine AS build
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
# next.config.mjs 的 rewrites 在构建时固化；同域部署时由 Caddy 直接转发，这里仅作兜底。
ARG API_INTERNAL_URL=http://api:8080
ENV API_INTERNAL_URL=$API_INTERNAL_URL
COPY --from=deps /app/node_modules ./node_modules
COPY package.json package-lock.json tsconfig.json next.config.mjs next-env.d.ts ./
COPY app ./app
COPY components ./components
COPY lib ./lib
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 PORT=3000 HOSTNAME=0.0.0.0
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
USER node
EXPOSE 3000
CMD ["node", "server.js"]
