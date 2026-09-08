import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, beforeEach } from 'vitest';

//  Every test starts from an empty DOM and an empty localStorage. The cart
//  persists to localStorage, so one test's cart leaking into the next is a real
//  hazard rather than a theoretical one.
afterEach(() => cleanup());
beforeEach(() => localStorage.clear());
