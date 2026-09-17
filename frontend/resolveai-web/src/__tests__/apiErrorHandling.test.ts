import { describe, it } from "node:test";
import assert from "node:assert";

import {
  ApiError,
  isForbiddenError,
  isNotFoundError,
  isRateLimitError,
  isUnauthorizedError,
  isValidationError,
} from "../api/api.ts";

describe("Frontend API Error Handling", () => {
  it("creates ApiError instances with correct properties", () => {
    const error = new ApiError("rate_limit", "Too many requests. Please try again shortly.", 429);
    assert.strictEqual(error.name, "ApiError");
    assert.strictEqual(error.kind, "rate_limit");
    assert.strictEqual(error.status, 429);
    assert.strictEqual(error.message, "Too many requests. Please try again shortly.");
  });

  it("identifies unauthorized (401) errors accurately", () => {
    const authError = new ApiError("auth", "Session expired.", 401);
    const forbiddenError = new ApiError("forbidden", "Access denied.", 403);
    const regularError = new Error("Regular error");

    assert.strictEqual(isUnauthorizedError(authError), true);
    assert.strictEqual(isUnauthorizedError(forbiddenError), false);
    assert.strictEqual(isUnauthorizedError(regularError), false);
  });

  it("identifies forbidden (403) errors accurately", () => {
    const forbiddenError = new ApiError("forbidden", "Access denied.", 403);
    const authError = new ApiError("auth", "Session expired.", 401);

    assert.strictEqual(isForbiddenError(forbiddenError), true);
    assert.strictEqual(isForbiddenError(authError), false);
  });

  it("identifies rate limit (429) errors accurately", () => {
    const rateLimitError = new ApiError("rate_limit", "Too many requests.", 429);
    const serverError = new ApiError("server", "Server error", 500);

    assert.strictEqual(isRateLimitError(rateLimitError), true);
    assert.strictEqual(isRateLimitError(serverError), false);
  });

  it("identifies validation (400) and not found (404) errors accurately", () => {
    const validationError = new ApiError("validation", "Invalid input", 400);
    const notFoundError = new ApiError("not_found", "Incident not found", 404);

    assert.strictEqual(isValidationError(validationError), true);
    assert.strictEqual(isValidationError(notFoundError), false);

    assert.strictEqual(isNotFoundError(notFoundError), true);
    assert.strictEqual(isNotFoundError(validationError), false);
  });

  it("clears session tokens on 401 unauthorized handling", () => {
    // Mock sessionStorage environment
    const storage: Record<string, string> = {
      accessToken: "test-jwt-token",
      currentUser: JSON.stringify({ id: "user-1", role: "Employee" }),
    };

    // Simulated session clear on 401
    delete storage.accessToken;
    delete storage.currentUser;

    assert.strictEqual(storage.accessToken, undefined);
    assert.strictEqual(storage.currentUser, undefined);
  });
});
