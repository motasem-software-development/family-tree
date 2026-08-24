import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { SearchableSelect, type SelectOption } from './SearchableSelect'

const OPTIONS: SelectOption[] = [
  { value: '1', label: '🇵🇸 فلسطين', keywords: ['PS', 'Palestine', '+970'] },
  { value: '2', label: '🇹🇷 تركيا', keywords: ['TR', 'Türkiye', '+90'] },
  { value: '3', label: '🇯🇵 اليابان', keywords: ['JP', 'Japan', '+81'] },
]

const Harness = ({
  initial = '',
  onChange = () => {},
}: {
  initial?: string
  onChange?: (value: string) => void
}) => {
  const [value, setValue] = useState(initial)
  return (
    <SearchableSelect
      id="country"
      ariaLabel="Country"
      value={value}
      options={OPTIONS}
      emptyLabel="Not recorded"
      noResultsLabel="No matches"
      onChange={(next) => {
        setValue(next)
        onChange(next)
      }}
      controlStyle={{}}
    />
  )
}

const open = async () => {
  const input = screen.getByRole('combobox', { name: 'Country' })
  await userEvent.click(input)
  return input
}

describe('SearchableSelect', () => {
  it('shows every option once opened, plus the clear row', async () => {
    render(<Harness />)
    await open()

    expect(screen.getAllByRole('option')).toHaveLength(OPTIONS.length + 1)
    expect(screen.getByRole('option', { name: 'Not recorded' })).toBeInTheDocument()
  })

  it('filters by a name in the other language', async () => {
    render(<Harness />)
    const input = await open()

    await userEvent.type(input, 'japan')

    const shown = screen.getAllByRole('option').map((option) => option.textContent)
    expect(shown).toEqual(['🇯🇵 اليابان'])
  })

  it('filters by a name typed without its Latin accent', async () => {
    render(<Harness />)
    const input = await open()

    await userEvent.type(input, 'turkiye')

    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['🇹🇷 تركيا'])
  })

  it('filters by ISO code', async () => {
    render(<Harness />)
    const input = await open()

    await userEvent.type(input, 'ps')

    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['🇵🇸 فلسطين'])
  })

  it('filters by a dialing code typed without the plus', async () => {
    render(<Harness />)
    const input = await open()

    await userEvent.type(input, '970')

    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['🇵🇸 فلسطين'])
  })

  it('reports the chosen value and shows its label', async () => {
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)
    const input = await open()

    await userEvent.type(input, 'japan')
    await userEvent.click(screen.getByRole('option', { name: '🇯🇵 اليابان' }))

    expect(onChange).toHaveBeenCalledWith('3')
    expect(input).toHaveValue('🇯🇵 اليابان')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('clears the value through the empty row', async () => {
    const onChange = vi.fn()
    render(<Harness initial="1" onChange={onChange} />)
    await open()

    await userEvent.click(screen.getByRole('option', { name: 'Not recorded' }))

    expect(onChange).toHaveBeenCalledWith('')
  })

  it('picks the highlighted option with the keyboard', async () => {
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)
    const input = await open()

    // Past the clear row, onto the first country.
    await userEvent.keyboard('{ArrowDown}{Enter}')

    expect(onChange).toHaveBeenCalledWith('1')
    expect(input).toHaveValue('🇵🇸 فلسطين')
  })

  it('does not submit the surrounding form when Enter picks an option', async () => {
    const onSubmit = vi.fn((event: React.FormEvent) => event.preventDefault())
    render(
      <form onSubmit={onSubmit}>
        <Harness />
      </form>,
    )
    await open()

    await userEvent.keyboard('{ArrowDown}{Enter}')

    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('says so when nothing matches', async () => {
    render(<Harness />)
    const input = await open()

    await userEvent.type(input, 'zzzz')

    expect(screen.queryAllByRole('option')).toHaveLength(0)
    expect(screen.getByText('No matches')).toBeInTheDocument()
  })

  it('abandons the query and restores the selection on Escape', async () => {
    render(<Harness initial="1" />)
    const input = await open()

    await userEvent.type(input, 'japan')
    await userEvent.keyboard('{Escape}')

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(input).toHaveValue('🇵🇸 فلسطين')
  })

  it('opens nothing while disabled', async () => {
    render(
      <SearchableSelect
        id="country"
        ariaLabel="Country"
        value=""
        options={OPTIONS}
        noResultsLabel="No matches"
        disabled
        onChange={() => {}}
        controlStyle={{}}
      />,
    )

    await userEvent.click(screen.getByRole('combobox', { name: 'Country' }))

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })
})
