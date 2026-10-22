/**
 * Project-specific customer segment options.
 * Key: project name (case-insensitive match against authStore.projectNames)
 * Value: list of segment strings shown in dropdowns
 *
 * To add a new segment: just add a string to the relevant array.
 */
export const PROJECT_SEGMENTS: Record<string, string[]> = {
  // Proje 2026-10'da EMS → Liftdesk olarak yeniden adlandırıldı; iki ad da aynı listeyi görür.
  EMS: [
    'Asansör Firması',
  ],
  Liftdesk: [
    'Asansör Firması',
  ],
  Rezerval: [
    'Tekil Restoran',
    'Zincir Restoran',
    'Cafe',
    'Club & Beach',
    'Otel',
    'Spa',
  ],
};

/** Returns segment list for the given project name, or empty array if not configured. */
export function getSegmentsForProject(projectName: string | undefined): string[] {
  if (!projectName) return [];
  const key = Object.keys(PROJECT_SEGMENTS).find(
    (k) => k.toLowerCase() === projectName.toLowerCase()
  );
  return key ? PROJECT_SEGMENTS[key] : [];
}

/** Liftdesk (eski adıyla EMS) projesi mi? Segment + Potansiyel Label alanları yalnız bu projede gösterilir. */
export function isLiftdeskProjectName(projectName: string | undefined): boolean {
  const n = projectName?.trim().toLowerCase();
  return n === 'liftdesk' || n === 'ems';
}
