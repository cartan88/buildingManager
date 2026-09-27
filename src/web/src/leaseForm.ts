/**
 * Rent to show after the user picks a different unit on the new-lease form: follow the new unit's
 * asking rent, unless the user has typed their own figure (anything other than the previous unit's rent).
 */
export function rentAfterUnitChange(currentRent: string, previousUnitRent: number | undefined, newUnitRent: number | undefined): string {
  const typedByUser = currentRent !== '' && (previousUnitRent === undefined || Number(currentRent) !== previousUnitRent)
  if (typedByUser) return currentRent
  return newUnitRent === undefined ? '' : String(newUnitRent)
}
