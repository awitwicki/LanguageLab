import re
from pathlib import Path
from typing import Set, Dict
from lxml import etree
import nltk
from nltk.stem import WordNetLemmatizer
from nltk.corpus import wordnet, stopwords
from nltk.tokenize import word_tokenize
from nltk import pos_tag
from functools import lru_cache
from tqdm import tqdm

# Download required NLTK data
nltk.download('punkt', quiet=True)
nltk.download('wordnet', quiet=True)
nltk.download('averaged_perceptron_tagger_eng', quiet=True)
nltk.download('stopwords', quiet=True)

# Initialize tools
lemmatizer = WordNetLemmatizer()
stop_words = set(stopwords.words('english'))

# Common prefixes and suffixes for filtering
prefixes = {'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten'}
suffixes = {'th', 'st', 'nd', 'rd'}

# Cache for lemmas to improve performance
lemma_cache: Dict[str, str] = {}


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


@lru_cache(maxsize=10000)
def get_lemma(word: str) -> str:
    """Get the lemma (base form) of a word with POS tagging."""
    if not word or len(word) < 2:
        return word

    # Check cache first
    if word in lemma_cache:
        return lemma_cache[word]

    # Tag part of speech
    pos_tag_result = pos_tag([word])
    if not pos_tag_result:
        lemma_cache[word] = word
        return word

    treebank_tag = pos_tag_result[0][1]
    wordnet_pos = get_wordnet_pos(treebank_tag)

    # Lemmatize with appropriate POS
    lemma = lemmatizer.lemmatize(word, pos=wordnet_pos)

    # If lemmatization didn't change the word, try with default (noun)
    if lemma == word and wordnet_pos != wordnet.NOUN:
        lemma = lemmatizer.lemmatize(word)

    # Cache the result
    lemma_cache[word] = lemma.lower()

    return lemma_cache[word]


def clean_word(word: str) -> str:
    """Clean word from punctuation and convert to lowercase."""
    # Remove punctuation except hyphens (for compound words)
    cleaned = re.sub(r'[^\w\s-]', '', word.lower())

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


def get_word_base_forms(word: str) -> Set[str]:
    """Get base forms of a word including lemmas and split parts."""
    base_forms = set()

    # Clean the word first
    cleaned_word = clean_word(word)

    if not cleaned_word:
        return base_forms

    # Get lemma of the full word
    lemma = get_lemma(cleaned_word)
    if lemma and len(lemma) > 1:
        base_forms.add(lemma)

    # Handle compound words
    if '-' in cleaned_word:
        parts = split_compound_word(cleaned_word)
        for part in parts:
            part_lemma = get_lemma(part)
            if part_lemma and len(part_lemma) > 1:
                base_forms.add(part_lemma)
    else:
        # For non-compound words, just add the lemma
        if lemma and len(lemma) > 1:
            base_forms.add(lemma)

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


def process_word(word: str, vocabulary: Set[str]) -> None:
    """Process a single word and add its base form to vocabulary."""
    if not is_valid_word(word):
        return

    cleaned_word = clean_word(word)
    if not cleaned_word:
        return

    # Get base forms (lemmas)
    base_forms = get_word_base_forms(cleaned_word)

    # Add valid base forms to vocabulary
    for base_form in base_forms:
        if is_valid_word(base_form) and len(base_form) > 1:
            vocabulary.add(base_form)


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

    # Tokenize text
    print("Tokenizing text...")
    words = word_tokenize(text)

    # Process words and build vocabulary
    print("Processing words...")
    vocabulary = set()

    for word in tqdm(words, desc="Extracting vocabulary"):
        # Check if it's a compound word
        if '-' in word:
            # Process both the full word and its parts
            process_word(word, vocabulary)

            # Also process individual parts
            parts = split_compound_word(word)
            for part in parts:
                process_word(part, vocabulary)
        else:
            # Process single word
            process_word(word, vocabulary)

    # Sort vocabulary alphabetically
    sorted_vocabulary = sorted(vocabulary)

    # Save to file
    print(f"Saving {len(sorted_vocabulary)} words to {output_file}...")
    with open(output_file, 'w', encoding='utf-8') as f:
        for word in sorted_vocabulary:
            f.write(f"{word}\n")

    # Print statistics
    print("\nProcessing complete!")
    print(f"Total words processed: {len(words)}")
    print(f"Unique base forms found: {len(sorted_vocabulary)}")
    print(f"Results saved to: {output_file}")

if __name__ == "__main__":
    main()