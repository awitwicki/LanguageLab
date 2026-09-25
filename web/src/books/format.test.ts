import { describe, expect, it } from 'vitest'
import { epub3Bytes, zipBytes } from '../test/epubFixtures'
import { READER_BOOK_XML, bytesOf } from '../test/readerFixtures'
import { readBookSource, stripBookExtension } from './format'
import { BookFormatError } from './formatError'

describe('stripBookExtension', () => {
  it('drops a book extension, single or double, whatever its case', () => {
    expect(stripBookExtension('deaths-end.fb2')).toBe('deaths-end')
    expect(stripBookExtension('deaths-end.EPUB')).toBe('deaths-end')
    expect(stripBookExtension('deaths-end.fb2.zip')).toBe('deaths-end')
    expect(stripBookExtension('deaths-end.zip')).toBe('deaths-end')
    expect(stripBookExtension('deaths.end')).toBe('deaths.end')
  })
})

describe('readBookSource', () => {
  it('reads a plain fb2', () => {
    const source = readBookSource(bytesOf(READER_BOOK_XML), 'deaths-end.fb2')

    expect(source.format).toBe('fb2')
    expect(source.format === 'fb2' && source.xml).toContain('FictionBook')
    expect(source.format === 'fb2' && source.fallbackTitle).toBe('deaths-end')
  })

  it('reads an epub', () => {
    const source = readBookSource(epub3Bytes(), 'deaths-end.epub')

    expect(source.format).toBe('epub')
    expect(source.format === 'epub' && source.book.title).toBe("Death's End")
  })

  it('reads a zipped fb2', () => {
    const source = readBookSource(zipBytes({ 'deaths-end.fb2': READER_BOOK_XML }), 'deaths-end.fb2.zip')

    expect(source.format === 'fb2' && source.xml).toContain('FictionBook')
  })

  // Review Focus 3: the inner file's own encoding, not the archive's.
  it('decodes a zipped windows-1251 fb2 from the entry bytes', () => {
    const head = '<?xml version="1.0" encoding="windows-1251"?><FictionBook><body><section><p>'
    const tail = ' said the guard.</p></section></body></FictionBook>'
    // "Привіт" in windows-1251.
    const inner = new Uint8Array([
      ...new TextEncoder().encode(head),
      0xcf, 0xf0, 0xe8, 0xe2, 0xb3, 0xf2,
      ...new TextEncoder().encode(tail),
    ])

    const source = readBookSource(zipBytes({ 'guard.fb2': inner }), 'guard.fb2.zip')

    expect(source.format === 'fb2' && source.xml).toContain('Привіт said the guard.')
  })

  it('goes by the bytes, not by the extension', () => {
    const source = readBookSource(epub3Bytes(), 'mislabelled.fb2')

    expect(source.format).toBe('epub')
  })

  it('names DRM for what it is', () => {
    const encryption = `<encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <EncryptedData xmlns="http://www.w3.org/2001/04/xmlenc#">
    <EncryptionMethod Algorithm="http://www.w3.org/2001/04/xmlenc#aes256-cbc"/>
    <CipherData><CipherReference URI="OEBPS/text/ch01.xhtml"/></CipherData>
  </EncryptedData>
</encryption>`
    const bytes = epub3Bytes({ 'META-INF/encryption.xml': encryption })

    try {
      readBookSource(bytes, 'locked.epub')
      expect.unreachable()
    } catch (e) {
      expect((e as BookFormatError).problem).toBe('encrypted')
      expect((e as Error).message).toBe("This book is protected by DRM and can't be opened.")
    }
  })

  it('refuses a book whose encryption.xml cannot be told apart from real DRM', () => {
    // No EncryptionMethod at all: not recognisably font-obfuscation-only, so fail safe.
    const bytes = epub3Bytes({ 'META-INF/encryption.xml': '<encryption/>' })

    expect(() => readBookSource(bytes, 'locked.epub')).toThrow(BookFormatError)
  })

  // Review: font obfuscation (IDPF and Adobe's algorithms) is common in DRM-free retail and
  // InDesign-built epubs and must not be mistaken for real DRM — the text itself is not encrypted.
  it('opens a DRM-free epub whose encryption.xml only obfuscates embedded fonts', () => {
    const encryption = `<encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <EncryptedData xmlns="http://www.w3.org/2001/04/xmlenc#">
    <EncryptionMethod Algorithm="http://www.idpf.org/2008/embedding"/>
    <CipherData><CipherReference URI="fonts/font1.otf"/></CipherData>
  </EncryptedData>
  <EncryptedData xmlns="http://www.w3.org/2001/04/xmlenc#">
    <EncryptionMethod Algorithm="http://ns.adobe.com/pdf/enc#RC"/>
    <CipherData><CipherReference URI="fonts/font2.otf"/></CipherData>
  </EncryptedData>
</encryption>`

    const source = readBookSource(epub3Bytes({ 'META-INF/encryption.xml': encryption }), 'font-obfuscated.epub')

    expect(source.format).toBe('epub')
  })

  // Review Focus 4: an epub mimetype with nothing behind it must not throw a TypeError.
  it('refuses a zip that claims to be an epub but has no container', () => {
    expect(() => readBookSource(zipBytes({ mimetype: 'application/epub+zip' }), 'broken.epub')).toThrow(
      "This file isn't a readable fb2 or epub book.",
    )
  })

  it('refuses a zip that is neither, and one with two fb2 files in it', () => {
    expect(() => readBookSource(zipBytes({ 'notes.txt': 'hello' }), 'x.zip')).toThrow(BookFormatError)
    expect(() => readBookSource(zipBytes({ 'a.fb2': READER_BOOK_XML, 'b.fb2': READER_BOOK_XML }), 'two.zip')).toThrow(
      BookFormatError,
    )
  })

  it('refuses a corrupt archive and an empty file', () => {
    expect(() => readBookSource(new Uint8Array([0x50, 0x4b, 0x03, 0x04, 0x00, 0x01]).buffer, 'x.epub')).toThrow(
      BookFormatError,
    )
    expect(readBookSource(new ArrayBuffer(0), 'empty.fb2').format).toBe('fb2')
  })
})
