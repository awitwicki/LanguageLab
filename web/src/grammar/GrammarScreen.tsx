import { useEffect, useState } from 'react'
import { api, type GrammarTopic } from '../api/client'
import { formatInt, plural } from '../lib/format'
import './GrammarScreen.css'

/** The goals the picker offers, lowest first — a topic is shown when its level is not above the goal. */
const GOAL_LEVELS = ['A1', 'A2', 'B1', 'B2']

interface Props {
  /** The account's saved goal. */
  goal: string
  /** The goal was saved; the account is refreshed so the next visit starts from it. */
  onGoalSaved: () => void
  onOpenTopic: (key: string) => void
}

interface Section {
  title: string
  topics: GrammarTopic[]
}

/** Consecutive topics of one section, keeping the catalog's order. */
function sectionsOf(topics: GrammarTopic[]): Section[] {
  const sections: Section[] = []

  for (const topic of topics) {
    const last = sections.at(-1)

    if (last?.title === topic.section) {
      last.topics.push(topic)
    } else {
      sections.push({ title: topic.section, topics: [topic] })
    }
  }

  return sections
}

/// The mode's front page: the whole syllabus in learning order, grouped by section. Written topics
/// open; planned ones are greyed out until their content ships. Nothing is stored, so there is no
/// progress to show yet.
export function GrammarScreen({ goal: savedGoal, onGoalSaved, onOpenTopic }: Props) {
  const [topics, setTopics] = useState<GrammarTopic[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [goal, setGoal] = useState(savedGoal)
  const [saveError, setSaveError] = useState<string | null>(null)

  // The list follows the pick at once; a failed save puts the saved goal back.
  const pickGoal = (level: string) => {
    if (level === goal) {
      return
    }

    const previous = goal
    setGoal(level)
    setSaveError(null)
    api
      .setGrammarGoal(level)
      .then(onGoalSaved)
      .catch((e) => {
        setGoal(previous)
        setSaveError(String(e))
      })
  }

  useEffect(() => {
    api
      .getGrammarTopics()
      .then(setTopics)
      .catch((e) => setError(String(e)))
  }, [])

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!topics) {
    return <p className="footnote">Loading…</p>
  }

  const reach = GOAL_LEVELS.indexOf(goal)
  const sections = sectionsOf(topics.filter((t) => GOAL_LEVELS.indexOf(t.level) <= reach))

  return (
    <>
      <h1 className="large-title">Grammar</h1>
      <p className="grammar-intro">Short rules, each with a few exercises. Pick the form that fits the sentence.</p>

      <div className="grammar-goal-row">
        <span className="footnote" id="grammar-goal-label">
          Your goal
        </span>
        <div className="grammar-goal" role="group" aria-labelledby="grammar-goal-label">
          {GOAL_LEVELS.map((level) => (
            <button
              key={level}
              type="button"
              className={`btn ${level === goal ? 'btn-primary' : 'btn-secondary'}`}
              aria-pressed={level === goal}
              onClick={() => pickGoal(level)}
            >
              {level}
            </button>
          ))}
        </div>
      </div>
      {saveError && <p className="error">{saveError}</p>}

      {sections.length === 0 && <p className="footnote grammar-empty">No topics at {goal} or below yet.</p>}

      {sections.map((section) => (
        <section key={section.title} className="grammar-section">
          <h2 className="title">{section.title}</h2>
          <ul className="grammar-topics">
            {section.topics.map((topic) => (
              <li key={topic.key}>
                {topic.planned ? (
                  <div className="grammar-topic is-planned">
                    <span className="grammar-topic-level">{topic.level}</span>
                    <span className="headline grammar-topic-title">{topic.title}</span>
                    <span className="footnote grammar-topic-soon">Coming later</span>
                  </div>
                ) : (
                  <button type="button" className="grammar-topic" onClick={() => onOpenTopic(topic.key)}>
                    <span className="grammar-topic-level">{topic.level}</span>
                    <span className="headline grammar-topic-title">{topic.title}</span>
                    <span className="footnote num">
                      {formatInt(topic.exercises.length)} {plural(topic.exercises.length, 'exercise', 'exercises')}
                    </span>
                  </button>
                )}
              </li>
            ))}
          </ul>
        </section>
      ))}
    </>
  )
}
