import re
from pathlib import Path
from typing import Set, Dict
from lxml import etree
from translate import Translator
import nltk
from nltk.stem import PorterStemmer
from nltk.tokenize import word_tokenize
from tqdm import tqdm

# Download required NLTK data
nltk.download('punkt')


translator = Translator(to_lang='uk')
stemmer = PorterStemmer()


prefixes = {'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten'}
suffixes = {'th', 'st', 'nd', 'rd'}
word_families = {}  # Dictionary to store word families

def clean_word(word: str) -> str:
    """Clean word from punctuation and convert to lowercase."""
    # Remove all punctuation and convert to lowercase
    return re.sub(r'[^\w\s-]', '', word.lower())


def get_base_word(word: str) -> str:
    """Get the shortest word from a word family."""
    stem = stemmer.stem(word)
    if stem not in word_families:
        word_families[stem] = word
    elif len(word) < len(word_families[stem]):
        word_families[stem] = word
    return word_families[stem]


def split_compound_word(word: str) -> Set[str]:
    """Split compound words and return individual parts."""
    parts = set()
    # Split by hyphen
    for part in word.split('-'):
        # Clean each part
        clean_part = clean_word(part)

        # Remove numeric parts and common prefixes/suffixes
        clean_part = re.sub(r'[0-9]+', '', clean_part)

        # Check if the part is a number word with a suffix
        skip = False
        for prefix in prefixes:
            if clean_part.startswith(prefix):
                remaining = clean_part[len(prefix):]
                if remaining in suffixes or not remaining:
                    skip = True
                    break

        if not skip and clean_part.isalpha() and len(clean_part) > 2:
            parts.add(clean_part)

    return parts


def get_word_base_forms(word: str) -> Set[str]:
    """Get base forms of a word including stem and split parts."""
    base_forms = set()

    # Clean the word first
    cleaned_word = clean_word(word)

    # Add the original clean word if it's not a compound word
    if '-' not in cleaned_word:
        base_forms.add(cleaned_word)

    # Add split parts for compound words
    base_forms.update(split_compound_word(cleaned_word))

    # Add stems for all forms
    stems = {stemmer.stem(w) for w in base_forms}
    base_forms.update(stems)

    return base_forms


def is_valid_word(word: str) -> bool:
    # Clean and check the word
    cleaned_word = clean_word(word)

    # Skip if empty or too short
    if not cleaned_word or len(cleaned_word) < 2:
        return False

    # Skip words that are too long (likely encoded strings)
    if len(cleaned_word) > 30:
        return False

    # Skip words that contain too many digits or special characters
    if sum(c.isdigit() or not c.isalpha() for c in cleaned_word) > 3:
        return False

    # Skip words that don't start with a letter
    if not cleaned_word[0].isalpha():
        return False

    return True


def extract_text_from_fb2(fb2_file: str) -> str:
    print("Parsing FB2 file...")
    tree = etree.parse(fb2_file)
    root = tree.getroot()
    text_elements = root.xpath('//text() | //p/text()')
    total_text = ' '.join(text_elements)
    total_text = total_text.replace('—', ' ')
    return total_text


def main():
    fb2_file = "book.fb2"
    output_file = "result/new_words.txt"

    Path(output_file).parent.mkdir(parents=True, exist_ok=True)

    text = extract_text_from_fb2(fb2_file)

    print("Tokenizing text...")
    words = word_tokenize(text)

    print("Collecting unique words...")
    for word in tqdm(words, desc="Processing words"):
        cleaned_word = clean_word(word)
        if '-' in cleaned_word:
            # For hyphenated words, add both the full word and its parts
            parts = split_compound_word(cleaned_word)
            if parts:  # Only add the compound word if it has valid parts
                for part in parts:
                    if is_valid_word(part):
                        get_base_word(part)
        elif is_valid_word(cleaned_word):
            get_base_word(cleaned_word)

    clean_words = set(word_families.values())

    print("Saving translations...")
    with open(output_file, 'w', encoding='utf-8') as f:
        for word in sorted(clean_words):
            f.write(f"{word}\n")

    print(f"Done! Processed {len(words)} total words, found {len(clean_words)} unique new words.")
    print(f"Results saved to {output_file}")

if __name__ == "__main__":
    main()
