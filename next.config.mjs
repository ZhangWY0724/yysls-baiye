/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  distDir: process.env.NODE_ENV === "development" ? ".next-dev" : ".next",
  async rewrites() {
    const api = process.env.API_INTERNAL_URL || "http://127.0.0.1:5080";
    return [
      { source: "/api/v1/:path*", destination: `${api}/api/v1/:path*` },
      { source: "/hubs/:path*", destination: `${api}/hubs/:path*` },
    ];
  },
};

export default nextConfig;
