import { renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import i18n from './index'
import { useDirection } from './useDirection'

describe('useDirection', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('ar')
  })

  it('reports rtl for Arabic and stamps the document', () => {
    const { result } = renderHook(() => useDirection())

    expect(result.current).toBe('rtl')
    expect(document.documentElement.dir).toBe('rtl')
    expect(document.documentElement.lang).toBe('ar')
  })

  it('reports ltr for English and restamps the document', async () => {
    await i18n.changeLanguage('en')
    const { result } = renderHook(() => useDirection())

    expect(result.current).toBe('ltr')
    expect(document.documentElement.dir).toBe('ltr')
    expect(document.documentElement.lang).toBe('en')
  })
})
