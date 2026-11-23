import type { Metadata, Viewport } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Mars Needs Water",
  description:
    "Earth and Mars at true scale. Drag the sea level down and watch the oceans move to Mars.",
};

export const viewport: Viewport = { themeColor: "#04060c", viewportFit: "cover" };

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="" />
        <link
          rel="stylesheet"
          href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@500;700&family=Manrope:wght@400;500;700&family=Unbounded:wght@700;900&display=swap"
        />
      </head>
      <body>{children}</body>
    </html>
  );
}
