/**
 * Project-specific customer segment options.
 * Key: project name (case-insensitive match against authStore.projectNames)
 * Value: list of segment strings shown in dropdowns
 *
 * To add a new segment: just add a string to the relevant array.
 */
export const PROJECT_SEGMENTS: Record<string, string[]> = {
  EMS: [
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
