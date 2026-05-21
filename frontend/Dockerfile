# =========================================================
#  Stage 1: Build SPA
# =========================================================
FROM node:20-alpine AS build
WORKDIR /app

# Install deps first for cache
COPY package.json package-lock.json ./
RUN npm ci --legacy-peer-deps

# Build
COPY . .
RUN npm run build

# =========================================================
#  Stage 2: Nginx serving + API reverse proxy
# =========================================================
FROM nginx:1.27-alpine AS runtime

# Static assets
COPY --from=build /app/dist /usr/share/nginx/html

# Nginx config (SPA fallback + /api + /hub proxy)
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

# Default nginx CMD is fine
