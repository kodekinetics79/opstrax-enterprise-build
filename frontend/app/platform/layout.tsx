export default function PlatformLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-midnight text-white">
      {children}
    </div>
  );
}
