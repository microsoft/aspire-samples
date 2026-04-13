import os
from typing import List, Optional
import psycopg
from psycopg.rows import dict_row
from fastapi import HTTPException

from models import User, UserCreate


class DatabaseManager:
    """Database manager for handling PostgreSQL operations."""

    @staticmethod
    async def get_connection():
        """Get database connection using DB_URI from Aspire."""
        db_uri = os.getenv("DB_URI")
        if not db_uri:
            raise HTTPException(status_code=500, detail="DB_URI not found")

        return await psycopg.AsyncConnection.connect(db_uri, row_factory=dict_row, autocommit=True)

    @staticmethod
    async def initialize_database():
        """Create database and users table if they don't exist."""
        print("=" * 60)
        print("Starting database initialization...")
        print("=" * 60)

        try:
            # Get environment variables
            postgres_uri = os.getenv("POSTGRES_URI")
            db_uri = os.getenv("DB_URI")
            db_name = os.getenv("DB_DATABASE", "db")

            print(f"Environment variables:")
            print(f"  POSTGRES_URI: {'✓ Set' if postgres_uri else '✗ Not found'}")
            print(f"  DB_URI: {'✓ Set' if db_uri else '✗ Not found'}")
            print(f"  DB_DATABASE: {db_name}")

            if not postgres_uri:
                raise Exception("POSTGRES_URI required for database initialization")

            # Step 1: Connect to PostgreSQL server
            print(f"\n[1/4] Connecting to PostgreSQL server...")
            async with await psycopg.AsyncConnection.connect(postgres_uri, autocommit=True) as conn:
                print(f"  ✓ Connected to PostgreSQL server")

                # Step 2: Check if database exists
                print(f"\n[2/4] Checking if database '{db_name}' exists...")
                async with conn.cursor() as cur:
                    await cur.execute(
                        "SELECT 1 FROM pg_database WHERE datname = %s",
                        (db_name,)
                    )
                    exists = await cur.fetchone()

                    if exists:
                        print(f"  ✓ Database '{db_name}' already exists")
                    else:
                        # Step 3: Create database
                        print(f"  → Database '{db_name}' does not exist, creating...")
                        await cur.execute(f'CREATE DATABASE "{db_name}"')
                        print(f"  ✓ Database '{db_name}' created successfully")

            # Step 4: Connect to target database and create table
            print(f"\n[3/4] Connecting to database '{db_name}'...")
            async with await DatabaseManager.get_connection() as conn:
                print(f"  ✓ Connected to database '{db_name}'")

                print(f"\n[4/4] Creating 'users' table if it doesn't exist...")
                async with conn.cursor() as cur:
                    await cur.execute("""
                        CREATE TABLE IF NOT EXISTS users (
                            id SERIAL PRIMARY KEY,
                            name VARCHAR(100) NOT NULL,
                            email VARCHAR(100) UNIQUE NOT NULL,
                            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                        )
                    """)
                print(f"  ✓ Table 'users' ready")

            print("\n" + "=" * 60)
            print("✓ Database initialization completed successfully")
            print("=" * 60)
        except Exception as e:
            print("\n" + "=" * 60)
            print(f"✗ Database initialization failed: {e}")
            print("=" * 60)
            raise
    
    @staticmethod
    async def check_health():
        """Check database connection health."""
        try:
            async with await DatabaseManager.get_connection() as conn:
                async with conn.cursor() as cur:
                    await cur.execute("SELECT 1")
            return {"status": "healthy", "database": "connected"}
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"Database error: {str(e)}")


class UserRepository:
    """Repository for user-related database operations."""
    
    @staticmethod
    async def get_all() -> List[User]:
        """Get all users from the database."""
        try:
            async with await DatabaseManager.get_connection() as conn:
                async with conn.cursor() as cur:
                    await cur.execute("SELECT id, name, email FROM users ORDER BY id")
                    rows = await cur.fetchall()
            return [User(**row) for row in rows]
        except Exception as e:
            raise HTTPException(status_code=500, detail=str(e))
    
    @staticmethod
    async def get_by_id(user_id: int) -> User:
        """Get a user by ID."""
        try:
            async with await DatabaseManager.get_connection() as conn:
                async with conn.cursor() as cur:
                    await cur.execute("SELECT id, name, email FROM users WHERE id = %s", (user_id,))
                    row = await cur.fetchone()
            
            if not row:
                raise HTTPException(status_code=404, detail="User not found")
            
            return User(**row)
        except HTTPException:
            raise
        except Exception as e:
            raise HTTPException(status_code=500, detail=str(e))
    
    @staticmethod
    async def create(user: UserCreate) -> User:
        """Create a new user."""
        try:
            async with await DatabaseManager.get_connection() as conn:
                async with conn.cursor() as cur:
                    await cur.execute(
                        "INSERT INTO users (name, email) VALUES (%s, %s) RETURNING id, name, email",
                        (user.name, user.email)
                    )
                    row = await cur.fetchone()
            
            return User(**row)
        except psycopg.errors.UniqueViolation:
            raise HTTPException(status_code=400, detail="Email already exists")
        except Exception as e:
            raise HTTPException(status_code=500, detail=str(e))
    
    @staticmethod
    async def delete(user_id: int) -> dict:
        """Delete a user by ID."""
        try:
            async with await DatabaseManager.get_connection() as conn:
                async with conn.cursor() as cur:
                    await cur.execute("DELETE FROM users WHERE id = %s RETURNING id", (user_id,))
                    row = await cur.fetchone()
            
            if not row:
                raise HTTPException(status_code=404, detail="User not found")
            
            return {"message": f"User {user_id} deleted"}
        except HTTPException:
            raise
        except Exception as e:
            raise HTTPException(status_code=500, detail=str(e))