import { describe, expect, it } from 'vitest'
import { amountToPayThrough, rentAfterUnitChange } from './leaseForm'

describe('rentAfterUnitChange', () => {
  it('fills in the asking rent when nothing is entered yet', () => {
    expect(rentAfterUnitChange('', undefined, 12000)).toBe('12000')
  })

  it('follows the new unit when the rent was auto-filled from the previous one', () => {
    expect(rentAfterUnitChange('12000', 12000, 15000)).toBe('15000')
  })

  it('keeps a rent the user typed themselves', () => {
    expect(rentAfterUnitChange('13500', 12000, 15000)).toBe('13500')
    expect(rentAfterUnitChange('13500', undefined, 15000)).toBe('13500')
  })

  it('clears an auto-filled rent when the unit is deselected', () => {
    expect(rentAfterUnitChange('12000', 12000, undefined)).toBe('')
  })
})

describe('amountToPayThrough', () => {
  it('adds up everything owed up to each charge', () => {
    const through = amountToPayThrough([{ id: 7, balance: 400 }, { id: 8, balance: 1000 }, { id: 9, balance: 1000.1 }])
    expect(through.get(7)).toBe(400) // partly paid oldest charge: only what's left of it
    expect(through.get(8)).toBe(1400)
    expect(through.get(9)).toBe(2400.1)
  })

  it('has nothing for paid charges', () => {
    expect(amountToPayThrough([]).size).toBe(0)
  })
})
