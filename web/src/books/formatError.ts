export type BookFormatProblem = 'invalid' | 'encrypted'

const MESSAGES: Record<BookFormatProblem, string> = {
  invalid: "This file isn't a readable fb2 or epub book.",
  encrypted: "This book is protected by DRM and can't be opened.",
}

/**
 * A file the app cannot read, with a message for the learner. Its own module so that the epub
 * parser, the format sniffer and the two parsers can all throw it without importing each other.
 */
export class BookFormatError extends Error {
  readonly problem: BookFormatProblem

  constructor(problem: BookFormatProblem) {
    super(MESSAGES[problem])
    this.problem = problem
  }
}
