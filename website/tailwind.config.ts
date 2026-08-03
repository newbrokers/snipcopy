import type { Config } from "tailwindcss";

const config: Config = {
  content: ["./src/**/*.{js,ts,jsx,tsx,mdx}"],
  theme: {
    extend: {
      colors: {
        ink: "#162033",
        steel: "#41516d",
        mint: "#3fbca2",
        coral: "#f46f5f",
        cloud: "#f5f7fb"
      },
      boxShadow: {
        soft: "0 20px 60px rgba(22, 32, 51, 0.12)"
      }
    }
  },
  plugins: []
};

export default config;
