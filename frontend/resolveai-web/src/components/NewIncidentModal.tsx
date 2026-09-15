import { useEffect, useState } from "react";
import type { FormEvent } from "react";

import {
  createIncident,
  getIncidentOptions,
  isUnauthorizedError,
} from "../api/api";
import type { IncidentOption } from "../api/api";

interface NewIncidentModalProps {
  onClose: () => void;
  onCreated: () => void | Promise<void>;
  onUnauthorized: () => void;
}

export default function NewIncidentModal({
  onClose,
  onCreated,
  onUnauthorized,
}: NewIncidentModalProps) {
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [priorityId, setPriorityId] = useState("");

  const [categories, setCategories] = useState<IncidentOption[]>([]);
  const [priorities, setPriorities] = useState<IncidentOption[]>([]);

  const [loadingOptions, setLoadingOptions] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    async function loadOptions() {
      try {
        setError("");

        const data = await getIncidentOptions();

        setCategories(data.categories);
        setPriorities(data.priorities);
      } catch (error) {
        if (isUnauthorizedError(error)) {
          onUnauthorized();
          return;
        }

        setError(
          error instanceof Error
            ? error.message
            : "Unable to load categories and priorities."
        );
      } finally {
        setLoadingOptions(false);
      }
    }

    loadOptions();
  }, [onUnauthorized]);

  async function handleSubmit(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    setError("");
    setSubmitting(true);

    try {
      await createIncident({
        title: title.trim(),
        description: description.trim(),
        categoryId,
        priorityId,
      });

      await onCreated();
    } catch (error) {
      if (isUnauthorizedError(error)) {
        onUnauthorized();
        return;
      }

      setError(
        error instanceof Error
          ? error.message
          : "Unable to create incident."
      );
      setSubmitting(false);
    }
  }

  return (
    <div
      className="modal-backdrop"
      onMouseDown={onClose}
    >
      <div
        className="modal-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="new-incident-title"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="modal-header">
          <div>
            <h2 id="new-incident-title">New Incident</h2>
            <p>
              Report an issue for investigation and resolution.
            </p>
          </div>

          <button
            type="button"
            className="close-button"
            onClick={onClose}
            aria-label="Close modal"
          >
            X
          </button>
        </div>

        {loadingOptions ? (
          <div className="modal-loading">
            Loading form...
          </div>
        ) : (
          <form className="modal-form" onSubmit={handleSubmit}>
            <label className="form-field">
              Incident title

              <input
                type="text"
                value={title}
                onChange={(event) =>
                  setTitle(event.target.value)
                }
                placeholder="Briefly describe the issue"
                maxLength={200}
                disabled={submitting}
                required
              />
            </label>

            <label className="form-field">
              Description

              <textarea
                value={description}
                onChange={(event) =>
                  setDescription(event.target.value)
                }
                placeholder="Provide more information about the issue..."
                rows={6}
                maxLength={5000}
                disabled={submitting}
                required
              />
            </label>

            <div className="form-row">
              <label className="form-field">
                Category

                <select
                  value={categoryId}
                  onChange={(event) =>
                    setCategoryId(event.target.value)
                  }
                  disabled={submitting}
                  required
                >
                  <option value="">Select category</option>

                  {categories.map((category) => (
                    <option
                      key={category.id}
                      value={category.id}
                    >
                      {category.name}
                    </option>
                  ))}
                </select>
              </label>

              <label className="form-field">
                Priority

                <select
                  value={priorityId}
                  onChange={(event) =>
                    setPriorityId(event.target.value)
                  }
                  disabled={submitting}
                  required
                >
                  <option value="">Select priority</option>

                  {priorities.map((priority) => (
                    <option
                      key={priority.id}
                      value={priority.id}
                    >
                      {priority.name}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            {error && (
              <div className="error-message">
                {error}
              </div>
            )}

            <div className="modal-actions">
              <button
                type="button"
                className="secondary-button"
                onClick={onClose}
                disabled={submitting}
              >
                Cancel
              </button>

              <button
                type="submit"
                className="primary-button"
                disabled={submitting}
              >
                {submitting
                  ? "Creating..."
                  : "Create Incident"}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
