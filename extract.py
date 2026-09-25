import re
from pathlib import Path
from typing import Set
from lxml import etree
import nltk
from nltk.stem import WordNetLemmatizer
from nltk.corpus import wordnet, stopwords
from nltk.tokenize import word_tokenize, sent_tokenize
from nltk import pos_tag
from functools import lru_cache
from tqdm import tqdm

# Download required NLTK data
nltk.download('punkt', quiet=True)
nltk.download('punkt_tab', quiet=True)
nltk.download('wordnet', quiet=True)
nltk.download('averaged_perceptron_tagger_eng', quiet=True)
nltk.download('stopwords', quiet=True)

# Initialize tools
lemmatizer = WordNetLemmatizer()
stop_words = set(stopwords.words('english'))

# Common prefixes and suffixes for filtering
prefixes = {'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten'}
suffixes = {'th', 'st', 'nd', 'rd'}


def get_wordnet_pos(treebank_tag: str) -> str:
    """Map POS tag to WordNet POS tag."""
    if treebank_tag.startswith('J'):
        return wordnet.ADJ
    elif treebank_tag.startswith('V'):
        return wordnet.VERB
    elif treebank_tag.startswith('N'):
        return wordnet.NOUN
    elif treebank_tag.startswith('R'):
        return wordnet.ADV
    else:
        return wordnet.NOUN  # default


@lru_cache(maxsize=20000)
def get_lemma(word: str, treebank_tag: str = '') -> str:
    """Get the lemma (base form) of a word.

    If treebank_tag is provided (from sentence-context pos_tag) we use it —
    this is far more accurate than tagging a word in isolation, which
    defaults to NN and leaves verb gerunds like 'surrounding' untouched.
    When no tag is provided (e.g. hyphenated compound parts) we fall back
    to single-word tagging.
    """
    if not word or len(word) < 2:
        return word

    if not treebank_tag:
        tagged = pos_tag([word])
        if not tagged:
            return word
        treebank_tag = tagged[0][1]

    wordnet_pos = get_wordnet_pos(treebank_tag)
    lemma = lemmatizer.lemmatize(word, pos=wordnet_pos)

    # If the context POS didn't reduce the word, try default (noun) as a safety net
    if lemma == word and wordnet_pos != wordnet.NOUN:
        lemma = lemmatizer.lemmatize(word)

    return lemma.lower()


def clean_word(word: str) -> str:
    """Clean word from punctuation and convert to lowercase.

    The apostrophe is kept (not stripped as generic punctuation): otherwise
    "don't"/"wasn't" become "dont"/"wasnt" — valid-looking words that no
    longer match the stopword list. Books mostly use the typographic
    apostrophe (’), so it's normalized to a straight ' first.
    """
    normalized = word.lower().replace('‘', "'").replace('’', "'")

    # Remove punctuation except hyphens and apostrophes (compounds/contractions)
    cleaned = re.sub(r"[^\w\s'-]", '', normalized)

    # Remove any remaining non-alphabetic characters at the edges
    cleaned = cleaned.strip("-_'\"")

    return cleaned


def split_compound_word(word: str) -> Set[str]:
    """Split compound words and return individual parts."""
    parts = set()

    # Split by hyphen
    hyphen_parts = word.split('-')

    for part in hyphen_parts:
        clean_part = clean_word(part)

        # Skip if too short or empty
        if len(clean_part) < 2 or not clean_part:
            continue

        # Remove numeric parts
        clean_part = re.sub(r'[0-9]+', '', clean_part)

        # Check if it's a number word with suffix (e.g., "first", "second")
        skip = False
        for prefix in prefixes:
            if clean_part.startswith(prefix):
                remaining = clean_part[len(prefix):]
                if remaining in suffixes or not remaining:
                    skip = True
                    break

        # Add valid parts
        if not skip and clean_part.isalpha() and len(clean_part) > 2:
            parts.add(clean_part)

    return parts


def get_word_base_forms(word: str, treebank_tag: str = '') -> Set[str]:
    """Get base forms of a word including lemmas and split parts."""
    base_forms = set()

    cleaned_word = clean_word(word)
    if not cleaned_word:
        return base_forms

    lemma = get_lemma(cleaned_word, treebank_tag)
    if lemma and len(lemma) > 1:
        base_forms.add(lemma)

    # Hyphenated compounds: lemmatize each part without context
    if '-' in cleaned_word:
        for part in split_compound_word(cleaned_word):
            part_lemma = get_lemma(part)
            if part_lemma and len(part_lemma) > 1:
                base_forms.add(part_lemma)

    return base_forms


def is_valid_word(word: str) -> bool:
    """Check if a word is valid for inclusion in the vocabulary."""
    # Clean the word
    cleaned_word = clean_word(word)

    # Skip if empty or too short
    if not cleaned_word or len(cleaned_word) < 2:
        return False

    # Skip stop words
    if cleaned_word in stop_words:
        return False

    # Skip contractions/possessives that survived cleaning without matching
    # a known stop word (e.g. "that's", "world's") — an apostrophe here means
    # it's not a plain word, and letting it through would add it to the
    # vocabulary with the apostrophe still in it.
    if "'" in cleaned_word:
        return False

    # Skip words that are too long (likely encoded strings or artifacts)
    if len(cleaned_word) > 30:
        return False

    # Skip words with too many digits or special characters
    if sum(1 for c in cleaned_word if c.isdigit() or not c.isalpha()) > 3:
        return False

    # Skip words that don't start with a letter
    if not cleaned_word[0].isalpha():
        return False

    # Skip common abbreviations and short forms
    if cleaned_word in {'ll', 've', 're', 't', 's', 'd', 'm'}:
        return False

    # Skip words that are all consonants or all vowels
    vowels = set('aeiouy')
    if all(c in vowels for c in cleaned_word) or all(c not in vowels for c in cleaned_word):
        if len(cleaned_word) > 3:  # Allow short words like "by", "my"
            return False

    return True


def extract_text_from_fb2(fb2_file: str) -> str:
    """Extract text content from FB2 file."""
    print("Parsing FB2 file...")
    try:
        tree = etree.parse(fb2_file)
        root = tree.getroot()

        # Define namespace for FB2
        ns = {'fb': 'http://www.gribuser.ru/xml/fictionbook/2.0'}

        # Extract text from relevant elements
        text_elements = []

        # Get all text from body sections
        bodies = root.xpath('//fb:body', namespaces=ns)
        for body in bodies:
            # Get all paragraph text
            paragraphs = body.xpath('.//fb:p', namespaces=ns)
            for p in paragraphs:
                if p.text:
                    text_elements.append(p.text)

            # Also get direct text content in sections
            sections = body.xpath('.//fb:section', namespaces=ns)
            for section in sections:
                # Get all text nodes in section
                text_nodes = section.xpath('.//text()')
                text_elements.extend(text_nodes)

        # Join all text
        total_text = ' '.join(text_elements)

        # Replace em-dash with space
        total_text = total_text.replace('—', ' ')

        # Remove extra whitespace
        total_text = re.sub(r'\s+', ' ', total_text)

        return total_text

    except Exception as e:
        print(f"Error parsing FB2 file: {e}")
        return ""


def process_word(word: str, vocabulary: Set[str], treebank_tag: str = '') -> None:
    """Process a single word and add its base form to vocabulary."""
    if not is_valid_word(word):
        return

    cleaned_word = clean_word(word)
    if not cleaned_word:
        return

    base_forms = get_word_base_forms(cleaned_word, treebank_tag)

    for base_form in base_forms:
        if is_valid_word(base_form) and len(base_form) > 1:
            vocabulary.add(base_form)


# -ing nouns that happen to match an unrelated verb under WordNet's morphy
# rules. WordNet strips 'evening' -> 'even' even though the time-of-day noun
# has nothing to do with the verb 'to even'. Extend this set if you spot
# similar homographs in future books.
ING_HOMOGRAPH_KEEP = {'evening', 'evenings'}


def consolidate_ing_forms(vocabulary: Set[str]) -> int:
    """Remove -ing / -ings forms whose verb base is already in the vocabulary.

    Two mechanisms:
      - -ing words: trust WordNet's verb-lemmatizer. It already protects real
        noun-ings like 'morning', 'ceiling', 'king', 'ring' (they don't reduce).
        A small ING_HOMOGRAPH_KEEP blocklist handles the escapes (currently
        just 'evening', which morphy wrongly strips to 'even').
      - -ings plurals: WordNet leaves plural gerund-nouns like 'surroundings',
        'belongings' unreduced, so we strip '-ings' (optionally restoring a
        trailing 'e') and keep the reduction only if the stem is a verb base
        already in the vocab.
    """
    to_remove = set()
    for w in vocabulary:
        if w in ING_HOMOGRAPH_KEEP:
            continue

        if w.endswith('ings') and len(w) >= 7:
            stem = w[:-4]
            for cand in (stem, stem + 'e'):
                if (
                    cand in vocabulary
                    and wordnet.synsets(cand, pos='v')
                    and lemmatizer.lemmatize(cand, 'v') == cand
                ):
                    to_remove.add(w)
                    break
            if w in to_remove:
                continue

        if w.endswith('ing') and len(w) >= 5:
            verb = lemmatizer.lemmatize(w, 'v')
            if verb != w and verb in vocabulary:
                to_remove.add(w)

    vocabulary -= to_remove
    return len(to_remove)


def main():
    """Main function to process FB2 file and extract vocabulary."""
    fb2_file = "book.fb2"
    output_file = "result/new_words.txt"

    # Create output directory if it doesn't exist
    Path(output_file).parent.mkdir(parents=True, exist_ok=True)

    # Extract text from FB2
    text = extract_text_from_fb2(fb2_file)
    if not text:
        print("No text extracted from the FB2 file.")
        return

    print(f"Extracted {len(text)} characters of text.")

    # Split into sentences so POS tagging has real context
    print("Tokenizing sentences...")
    sentences = sent_tokenize(text)
    print(f"Found {len(sentences)} sentences.")

    vocabulary: Set[str] = set()
    total_tokens = 0

    for sent in tqdm(sentences, desc="Extracting vocabulary"):
        tokens = word_tokenize(sent)
        total_tokens += len(tokens)
        tagged = pos_tag(tokens)
        for word, tag in tagged:
            if '-' in word:
                process_word(word, vocabulary, tag)
                for part in split_compound_word(word):
                    process_word(part, vocabulary)
            else:
                process_word(word, vocabulary, tag)

    # Unify '-ing'/'-ings' forms with their verb base when both are present
    removed = consolidate_ing_forms(vocabulary)

    sorted_vocabulary = sorted(vocabulary)

    print(f"Saving {len(sorted_vocabulary)} words to {output_file}...")
    with open(output_file, 'w', encoding='utf-8') as f:
        for word in sorted_vocabulary:
            f.write(f"{word}\n")

    print("\nProcessing complete!")
    print(f"Total tokens processed: {total_tokens}")
    print(f"Unified -ing/-ings forms removed: {removed}")
    print(f"Unique base forms found: {len(sorted_vocabulary)}")
    print(f"Results saved to: {output_file}")

if __name__ == "__main__":
    main()