/**
 * Rent to show after the user picks a different unit on the new-lease form: follow the new unit's
 * asking rent, unless the user has typed their own figure (anything other than the previous unit's rent).
 */
export function rentAfterUnitChange(currentRent: string, previousUnitRent: number | undefined, newUnitRent: number | undefined): string {
  const typedByUser = currentRent !== '' && (previousUnitRent === undefined || Number(currentRent) !== previousUnitRent)
  if (typedByUser) return currentRent
  return newUnitRent === undefined ? '' : String(newUnitRent)
}

/**
 * For each unpaid charge, how much a payment must be to pay it off. Payments go to the oldest unpaid
 * charges first, so paying one charge means paying everything still owed before it too.
 * `open` must be in allocation order (oldest due date first), as /open-charges returns it.
 */
export function amountToPayThrough(open: { id: number; balance: number }[]): Map<number, number> {
  const result = new Map<number, number>()
  let total = 0
  for (const c of open) {
    total = Math.round((total + c.balance) * 100) / 100 // stay in whole centavos
    result.set(c.id, total)
  }
  return result
}
