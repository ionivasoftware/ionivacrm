import { Construction } from 'lucide-react';

interface PlaceholderPageProps {
  title: string;
  description?: string;
}

export function PlaceholderPage({ title, description }: PlaceholderPageProps) {
  return (
    <div className="flex flex-col items-center justify-center min-h-[60vh] text-center">
      <Construction className="h-16 w-16 text-muted-foreground/30 mb-6" />
      <h1 className="text-2xl font-bold text-foreground mb-2">{title}</h1>
      <p className="text-muted-foreground max-w-md">
        {description ?? 'Bu sayfa yakında hazır olacak. Geliştirme devam ediyor.'}
      </p>
    </div>
  );
}
